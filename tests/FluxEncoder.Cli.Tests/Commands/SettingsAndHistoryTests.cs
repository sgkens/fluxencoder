using System.Text.Json;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Storage;
using FluxEncoder.Infrastructure.Tooling;
using Microsoft.Data.Sqlite;

namespace FluxEncoder.Cli.Tests.Commands;

/// <summary>
/// Settings, presets, <c>config</c>, and <c>history</c> through the real command surface, with all application files in
/// a temporary folder.
/// </summary>
public sealed class SettingsAndHistoryTests : IAsyncLifetime
{
    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-cfg-" + Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        if (Tools.IsComplete)
        {
            var output = await ProcessCapture.RunAsync(
                Tools.FFmpeg!.Path,
                ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=1", "-f", "lavfi", "-i", "sine=duration=1", "-c:v", "libx264", "-c:a", "aac", PathOf("clip.mkv")],
                TimeSpan.FromSeconds(60));
            Assert.True(output.Succeeded, output.StandardError);
        }
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private string PathOf(params string[] parts) => Path.Combine([_directory, .. parts]);

    private AppPaths Paths => new(PathOf("app", "config"), PathOf("app", "data"), PathOf("app", "cache"));

    private sealed record Run(int ExitCode, string Out, string Error);

    private Task<Run> RunAsync(params string[] args) => RunWithVariablesAsync(new Dictionary<string, string>(), args);

    private async Task<Run> RunWithVariablesAsync(IReadOnlyDictionary<string, string> variables, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error, name => variables.GetValueOrDefault(name), ErrorIsTerminal: false) { Paths = Paths };

        var exitCode = await CliApplication.RunAsync(args, host, TestContext.Current.CancellationToken);
        return new Run(exitCode, output.ToString(), error.ToString());
    }

    private async Task WriteSettingsAsync(string json)
    {
        Directory.CreateDirectory(Paths.Config);
        await File.WriteAllTextAsync(Paths.SettingsFile, json, TestContext.Current.CancellationToken);
    }

    private static void SkipWithoutFFmpeg() => Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");

    [Fact]
    public async Task ConfigPath_ListsFiles()
    {
        var run = await RunAsync("config", "path");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(Paths.SettingsFile, run.Out, StringComparison.Ordinal);
        Assert.Contains(Paths.HistoryDatabase, run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigInit_WritesTemplateOnceThenNeedsForce()
    {
        Assert.Equal(0, (await RunAsync("config", "init")).ExitCode);
        Assert.True(File.Exists(Paths.SettingsFile));

        var again = await RunAsync("config", "init");
        Assert.Equal(4, again.ExitCode);
        Assert.Contains("--force", again.Error, StringComparison.Ordinal);

        Assert.Equal(0, (await RunAsync("config", "init", "--force")).ExitCode);
        Assert.True(File.Exists(Paths.SettingsFile + ".bak"));
    }

    [Fact]
    public async Task ConfigShow_PrintsDefaultsWithoutFile()
    {
        var run = await RunAsync("config", "show");

        Assert.Equal(0, run.ExitCode);
        using var json = JsonDocument.Parse(run.Out);
        Assert.True(json.RootElement.GetProperty("history").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task SettingsDefaults_ApplyWhenFlagIsNotTyped()
    {
        SkipWithoutFFmpeg();
        await WriteSettingsAsync("""{ "encode": { "codec": "hevc", "quality": "fast" } }""");

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("libx265", run.Out, StringComparison.Ordinal);
        Assert.Contains("clip.hevc.mkv", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Precedence_IsTypedThenPresetThenSettings()
    {
        SkipWithoutFFmpeg();
        await WriteSettingsAsync("""
            { "encode": { "codec": "hevc", "audio-codec": "aac" },
              "presets": { "small": { "codec": "av1", "audio-codec": "opus" } } }
            """);

        var preset = await RunAsync("encode", PathOf("clip.mkv"), "--dry-run", "--preset", "small");
        Assert.Contains("-c:a:0 libopus", preset.Out, StringComparison.Ordinal);
        Assert.Contains("clip.av1.mkv", preset.Out, StringComparison.Ordinal);

        var typed = await RunAsync("encode", PathOf("clip.mkv"), "--dry-run", "--preset", "small", "--codec", "h264");
        Assert.Contains("libx264", typed.Out, StringComparison.Ordinal);
        Assert.Contains("-c:a:0 libopus", typed.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoConfig_IgnoresSettingsDefaults()
    {
        SkipWithoutFFmpeg();
        await WriteSettingsAsync("""{ "encode": { "codec": "hevc" } }""");

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--dry-run", "--no-config");

        Assert.Contains("libx264", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownPreset_IsUsageErrorListingPresets()
    {
        await WriteSettingsAsync("""{ "presets": { "web": { "codec": "h264" }, "archive": { "codec": "hevc" } } }""");

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--preset", "nope");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("available: archive, web", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidSettingValue_IsUsageErrorNamingItsSource()
    {
        await WriteSettingsAsync("""{ "encode": { "codec": "xvid" } }""");

        var run = await RunAsync("encode", PathOf("clip.mkv"));

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--codec xvid (settings.json)", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerInvocationOptionsCannotBePreset()
    {
        SkipWithoutFFmpeg();
        await WriteSettingsAsync("""{ "encode": { "output": "/tmp/x.mkv", "dry-run": true, "nonsense": 1 } }""");

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--dry-run", "--json");

        Assert.Equal(0, run.ExitCode);
        using var json = JsonDocument.Parse(run.Out);
        var notices = json.RootElement.GetProperty("notices").EnumerateArray().Select(n => n.GetString()).ToList();
        Assert.Equal(3, notices.Count);
        Assert.Equal(2, notices.Count(n => n!.Contains("cannot be preset", StringComparison.Ordinal)));
        Assert.Single(notices, n => n!.Contains("'nonsense' is not an encode option", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BrokenSettings_WarnVisiblyEvenWhenQuiet()
    {
        SkipWithoutFFmpeg();
        await WriteSettingsAsync("{ not json");

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--dry-run", "--quiet");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("not valid JSON", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Encode_IsRecordedInHistoryWithSamples()
    {
        SkipWithoutFFmpeg();

        Assert.Equal(0, (await RunAsync("encode", PathOf("clip.mkv"), "-o", PathOf("h1.mkv"), "-q", "fast")).ExitCode);
        Assert.Equal(3, (await RunAsync("encode", PathOf("missing.mkv"))).ExitCode);

        var list = await RunAsync("history", "list", "--json");
        Assert.Equal(0, list.ExitCode);
        using var json = JsonDocument.Parse(list.Out);
        var runs = json.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, runs.Count);
        Assert.Equal("failed", runs[0].GetProperty("outcome").GetString());
        Assert.Equal("input-not-found", runs[0].GetProperty("failureCategory").GetString());
        Assert.Equal("succeeded", runs[1].GetProperty("outcome").GetString());
        Assert.Equal("libx264", runs[1].GetProperty("videoEncoder").GetString());
        Assert.True(runs[1].GetProperty("outputBytes").GetInt64() > 0);
        Assert.Contains("ffmpeg", runs[1].GetProperty("command").GetString(), StringComparison.Ordinal);

        var table = await RunAsync("history", "list");
        Assert.Contains("succeeded", table.Out, StringComparison.Ordinal);

        Assert.Contains("Deleted 2 runs.", (await RunAsync("history", "clear")).Out, StringComparison.Ordinal);
        Assert.Equal("[]", (await RunAsync("history", "list", "--json")).Out.Trim());
    }

    [Fact]
    public async Task DryRunAndDisabledHistory_RecordNothing()
    {
        SkipWithoutFFmpeg();

        await RunAsync("encode", PathOf("clip.mkv"), "--dry-run");
        Assert.False(File.Exists(Paths.HistoryDatabase));

        await WriteSettingsAsync("""{ "history": { "enabled": false } }""");
        await RunAsync("encode", PathOf("clip.mkv"), "-o", PathOf("nohistory.mkv"), "-c", "copy");
        Assert.False(File.Exists(Paths.HistoryDatabase));
    }

    [Fact]
    public async Task HistoryList_WithoutDatabase()
    {
        var run = await RunAsync("history", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No runs recorded yet", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CredentialsInInputUrl_NeverReachOutputJsonOrHistory()
    {
        SkipWithoutFFmpeg();
        const string Password = "s3cretPW-91";
        var url = $"http://alice:{Password}@127.0.0.1:9/live.ts?token=t0ken-77";

        var human = await RunAsync("encode", url, "-o", PathOf("net.mkv"), "--verbose");
        var json = await RunAsync("encode", url, "-o", PathOf("net2.mkv"), "--json");

        Assert.Equal(3, human.ExitCode);
        foreach (var text in new[] { human.Out, human.Error, json.Out, json.Error })
        {
            Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain("t0ken-77", text, StringComparison.Ordinal);
        }

        Assert.Contains("alice:***@127.0.0.1", human.Error, StringComparison.Ordinal);

        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Paths.Data))
        {
            var bytes = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
            Assert.DoesNotContain(Password, bytes, StringComparison.Ordinal);
            Assert.DoesNotContain("t0ken-77", bytes, StringComparison.Ordinal);
        }

        var history = await RunAsync("history", "list", "--json");
        Assert.Contains("alice:***@", history.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FFmpegArg_IsPassedThroughToFFmpeg()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync("encode", PathOf("clip.mkv"), "-c", "copy", "-o", PathOf("titled.mkv"), "--ffmpeg-arg", "-metadata", "--ffmpeg-arg", "title=Raw Title");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("FluxEncoder cannot check what they do", run.Error, StringComparison.Ordinal);
        var probe = await ProcessCapture.RunAsync(Tools.FFprobe!.Path, ["-v", "error", "-show_entries", "format_tags=title", "-of", "csv=p=0", "-i", PathOf("titled.mkv")], TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal("Raw Title", probe.StandardOutput.Trim());
    }

    [Fact]
    public async Task ManagedMode_DisablesFFmpegArg()
    {
        SkipWithoutFFmpeg();

        var run = await RunWithVariablesAsync(new Dictionary<string, string> { ["FLUXENCODER_MANAGED"] = "1" }, "encode", PathOf("clip.mkv"), "-o", PathOf("m.mkv"), "--ffmpeg-arg", "-tune");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--ffmpeg-arg is disabled", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsSecurity_ConfinesInputsToAllowedRoots()
    {
        SkipWithoutFFmpeg();
        var allowed = PathOf("allowed");
        Directory.CreateDirectory(allowed);
        await WriteSettingsAsync($$"""{ "security": { "allowedRoots": [{{System.Text.Json.JsonSerializer.Serialize(allowed)}}] } }""");

        var run = await RunAsync("encode", PathOf("clip.mkv"), "-o", Path.Combine(allowed, "x.mkv"));

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("outside the allowed folders", run.Error, StringComparison.Ordinal);
    }
}
