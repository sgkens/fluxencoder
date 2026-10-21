using System.Text.Json;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Storage;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Cli.Tests.Commands;

/// <summary>
/// Runs the real command surface with captured stdout and stderr. Tests that encode need FFmpeg and skip without it.
/// </summary>
public sealed class EncodeCommandTests : IAsyncLifetime
{
    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-cli-" + Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        if (Tools.IsComplete)
        {
            await GenerateAsync("clip.mkv");
            await GenerateAsync(Path.Combine("batch", "one.mkv"));
            await GenerateAsync(Path.Combine("batch", "two.mkv"));
            await File.WriteAllTextAsync(Path.Combine(_directory, "batch", "broken.mkv"), "not media");
        }
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private async Task GenerateAsync(string name)
    {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var output = await ProcessCapture.RunAsync(
            Tools.FFmpeg!.Path,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=1", "-f", "lavfi", "-i", "sine=duration=1", "-c:v", "libx264", "-c:a", "aac", path],
            TimeSpan.FromSeconds(60));
        Assert.True(output.Succeeded, output.StandardError);
    }

    private sealed record Run(int ExitCode, string Out, string Error);

    private AppPaths Paths => new(PathOf("app", "config"), PathOf("app", "data"), PathOf("app", "cache"));

    private async Task<Run> RunAsync(IReadOnlyDictionary<string, string>? variables, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error, name => variables?.GetValueOrDefault(name), ErrorIsTerminal: false) { Paths = Paths };

        var exitCode = await CliApplication.RunAsync(args, host, TestContext.Current.CancellationToken);
        return new Run(exitCode, output.ToString(), error.ToString());
    }

    private Task<Run> RunAsync(params string[] args) => RunAsync(null, args);

    private string PathOf(params string[] parts) => Path.Combine([_directory, .. parts]);

    private static void SkipWithoutFFmpeg() => Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");

    [Fact]
    public async Task Help_ListsOnlyImplementedCommands()
    {
        var run = await RunAsync("--help");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("encode", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("browse", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("encode")]
    [InlineData("encode", "a.mkv", "--codec", "xvid")]
    [InlineData("encode", "a.mkv", "--audio", "english")]
    [InlineData("encode", "a.mkv", "--overwrite", "--skip-existing")]
    [InlineData("encode", "a.mkv", "--two-pass")]
    [InlineData("encode", "a.mkv", "--end", "10", "--duration", "5")]
    [InlineData("encode", "a.mkv", "--start", "20", "--end", "10")]
    [InlineData("encode", "a.mkv", "--codec", "copy", "--video-bitrate", "4M")]
    [InlineData("encode", "a.mkv", "--audio-bitrate", "128")]
    [InlineData("encode", "a.mkv", "--hardware-fallback")]
    [InlineData("encode", "a.mkv", "--json", "--quiet")]
    [InlineData("encode", "a.mkv", "--retries", "50")]
    [InlineData("encode", "a.mkv", "--start", "soon")]
    public async Task UsageErrors_ExitWith2OnStderrOnly(params string[] args)
    {
        var run = await RunAsync(args);

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("--help", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFFmpeg_ExitsWith5AndExplains()
    {
        var run = await RunAsync("encode", PathOf("clip.mkv"), "--ffmpeg", PathOf("no-ffmpeg"), "--ffprobe", PathOf("no-ffprobe"));

        Assert.Equal(5, run.ExitCode);
        Assert.Contains("not found", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFFmpeg_JsonReportsErrorObjectOnStdout()
    {
        var run = await RunAsync("encode", PathOf("clip.mkv"), "--json", "--ffmpeg", PathOf("no-ffmpeg"));

        Assert.Equal(5, run.ExitCode);
        Assert.Empty(run.Error);
        using var json = JsonDocument.Parse(run.Out);
        Assert.Equal("dependency-missing", json.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(5, json.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task Encode_WritesDefaultNamedOutputAndSummaryToStdout()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--quality", "fast");

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(PathOf("clip.h264.mkv")));
        Assert.Contains("clip.h264.mkv", run.Out, StringComparison.Ordinal);
        Assert.Contains("libx264 on CPU", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[", run.Out + run.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_directory, ".*partial*"));
    }

    [Fact]
    public async Task ExistingOutput_FailsByDefaultThenHonoursPolicies()
    {
        SkipWithoutFFmpeg();
        var target = PathOf("taken.mkv");
        await File.WriteAllTextAsync(target, "keep me", TestContext.Current.CancellationToken);

        var never = await RunAsync("encode", PathOf("clip.mkv"), "-o", target, "--codec", "copy");
        Assert.Equal(4, never.ExitCode);
        Assert.Contains("--overwrite", never.Error, StringComparison.Ordinal);
        Assert.Equal("keep me", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));

        var skip = await RunAsync("encode", PathOf("clip.mkv"), "-o", target, "--codec", "copy", "--skip-existing");
        Assert.Equal(0, skip.ExitCode);
        Assert.Contains("skipped", skip.Out, StringComparison.Ordinal);

        var rename = await RunAsync("encode", PathOf("clip.mkv"), "-o", target, "--codec", "copy", "--rename-existing");
        Assert.Equal(0, rename.ExitCode);
        Assert.True(File.Exists(PathOf("taken (1).mkv")));

        var overwrite = await RunAsync("encode", PathOf("clip.mkv"), "-o", target, "--codec", "copy", "--overwrite");
        Assert.Equal(0, overwrite.ExitCode);
        Assert.NotEqual("keep me", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DryRun_ShowsRedactedCommandAndWritesNothing()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--dry-run", "--codec", "hevc", "--start", "0.5");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("$ ", run.Out, StringComparison.Ordinal);
        Assert.Contains("-c:v:0 libx265", run.Out, StringComparison.Ordinal);
        Assert.Contains("-ss 0.5", run.Out, StringComparison.Ordinal);
        Assert.False(File.Exists(PathOf("clip.hevc.mkv")));
    }

    [Fact]
    public async Task Json_StdoutIsOneDocumentAndStderrIsEmpty()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--json", "--codec", "copy", "-o", PathOf("json.mkv"));

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Error);
        using var json = JsonDocument.Parse(run.Out);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        var job = root.GetProperty("jobs")[0];
        Assert.Equal("succeeded", job.GetProperty("outcome").GetString());
        Assert.Equal("copy", job.GetProperty("videoEncoder").GetString());
        Assert.Equal(PathOf("json.mkv"), job.GetProperty("output").GetString());
        Assert.True(job.GetProperty("commands")[0].GetArrayLength() > 3);
    }

    [Fact]
    public async Task Batch_ContinuesPastFailureAndExitsPartial()
    {
        SkipWithoutFFmpeg();
        var outputs = PathOf("out") + Path.DirectorySeparatorChar;

        var run = await RunAsync("encode", PathOf("batch"), "-o", outputs, "--codec", "copy", "--json");

        Assert.Equal(10, run.ExitCode);
        using var json = JsonDocument.Parse(run.Out);
        var summary = json.RootElement.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed").GetInt32());
        Assert.True(File.Exists(PathOf("out", "one.mkv")));
        Assert.True(File.Exists(PathOf("out", "two.mkv")));
        Assert.Equal("invalid-input", json.RootElement.GetProperty("jobs")[0].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task Batch_FailFastStopsAndCountsNotStarted()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync("encode", PathOf("batch"), "-o", PathOf("ff") + Path.DirectorySeparatorChar, "--codec", "copy", "--fail-fast");

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("2 not started", run.Out, StringComparison.Ordinal);
        Assert.False(Directory.Exists(PathOf("ff")) && Directory.GetFiles(PathOf("ff")).Length > 0);
    }

    [Fact]
    public async Task Batch_RejectsSingleOutputFileForSeveralInputs()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync("encode", PathOf("batch"), "-o", PathOf("all.mkv"));

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("must be a directory", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quiet_PrintsNothingOnSuccess()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--codec", "copy", "-o", PathOf("quiet.mkv"), "--quiet");

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Empty(run.Error);
    }

    [Fact]
    public async Task NonTerminal_PrintsPlainProgressMilestones()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync("encode", PathOf("clip.mkv"), "--quality", "fast", "-o", PathOf("progress.mkv"));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("%", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DumbTerminal_UsesAsciiSymbols()
    {
        SkipWithoutFFmpeg();

        var run = await RunAsync(new Dictionary<string, string> { ["TERM"] = "dumb" }, "encode", PathOf("clip.mkv"), "--codec", "copy", "-o", PathOf("ascii.mkv"));

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("+ ", run.Out, StringComparison.Ordinal);
        Assert.Contains("->", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("1", false)]
    public async Task Colour_OnlyOnTerminalsAndNeverWithNoColor(string? noColor, bool expectEscapes)
    {
        var output = new StringWriter();
        var host = new CliHost(output, TextWriter.Null, name => name == "NO_COLOR" ? noColor : null, ErrorIsTerminal: false, OutIsTerminal: true) { Paths = Paths };

        SkipWithoutFFmpeg();
        var exitCode = await CliApplication.RunAsync(["encode", PathOf("clip.mkv"), "--dry-run", "--codec", "copy"], host, TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal(expectEscapes, output.ToString().Contains('\u001b', StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostileFileName_CannotInjectTerminalEscapes()
    {
        SkipWithoutFFmpeg();
        var hostile = PathOf("evil\u001b]0;pwned\u0007\u001b[31m.mkv");
        File.Copy(PathOf("clip.mkv"), hostile);

        var run = await RunAsync("encode", hostile, "--codec", "copy", "-o", PathOf("safe.mkv"));

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain('\u001b', run.Out + run.Error);
        Assert.Contains("evil?]0;pwned??[31m.mkv", run.Out, StringComparison.Ordinal);
    }
}
