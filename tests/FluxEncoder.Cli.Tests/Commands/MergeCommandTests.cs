using System.Text.Json;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Infrastructure.Configuration;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Storage;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Cli.Tests.Commands;

public sealed class MergeCommandTests : IAsyncLifetime
{
    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-merge-" + Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        if (Tools.IsComplete)
        {
            await GenerateClipAsync("one.mkv", 160, 120);
            await GenerateClipAsync("two.mkv", 160, 120);
            await GenerateClipAsync("wide.mkv", 320, 180);
        }
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private async Task GenerateClipAsync(string name, int width, int height)
    {
        var path = Path.Combine(_directory, name);
        var output = await ProcessCapture.RunAsync(
            Tools.FFmpeg!.Path,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=25:duration=1", "-f", "lavfi", "-i", "sine=duration=1", "-c:v", "libx264", "-c:a", "aac", path],
            TimeSpan.FromSeconds(60));
        Assert.True(output.Succeeded, output.StandardError);
    }

    private sealed record Run(int ExitCode, string Out, string Error);

    private AppPaths Paths => new(PathOf("app", "config"), PathOf("app", "data"), PathOf("app", "cache"));

    private async Task<Run> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error, _ => null, ErrorIsTerminal: false) { Paths = Paths };

        var exitCode = await CliApplication.RunAsync(args, host, TestContext.Current.CancellationToken);
        return new Run(exitCode, output.ToString(), error.ToString());
    }

    private string PathOf(params string[] parts) => Path.Combine([_directory, .. parts]);

    private static void SkipWithoutFFmpeg() => Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");

    [Theory]
    [InlineData("merge")]
    [InlineData("merge", "a.mkv", "b.mkv")]
    [InlineData("merge", "a.mkv", "b.mkv", "-o", "out.mkv", "--mode", "copy", "--codec", "hevc")]
    [InlineData("merge", "a.mkv", "b.mkv", "-o", "out.mkv", "--overwrite", "--skip-existing")]
    public async Task UsageErrors_ExitWith2(params string[] args)
    {
        var run = await RunAsync(args);

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(run.Out);
    }

    [Fact]
    public async Task CompatibleInputs_MergeViaStreamCopy()
    {
        SkipWithoutFFmpeg();
        var outPath = PathOf("merged-copy.mkv");

        var run = await RunAsync("merge", PathOf("one.mkv"), PathOf("two.mkv"), "-o", outPath, "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(outPath));
        using var json = JsonDocument.Parse(run.Out);
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("succeeded").GetInt32());
    }

    [Fact]
    public async Task IncompatibleInputs_FailInCopyMode_AndNormalizeInAutoMode()
    {
        SkipWithoutFFmpeg();
        var copyOut = PathOf("merged-fail.mkv");

        var copyRun = await RunAsync("merge", PathOf("one.mkv"), PathOf("wide.mkv"), "-o", copyOut, "--mode", "copy", "--json");
        Assert.NotEqual(0, copyRun.ExitCode);
        Assert.False(File.Exists(copyOut));

        var autoOut = PathOf("merged-auto.mkv");
        var autoRun = await RunAsync("merge", PathOf("one.mkv"), PathOf("wide.mkv"), "-o", autoOut, "--mode", "auto", "--dry-run", "--json");
        Assert.Equal(0, autoRun.ExitCode);
        using var json = JsonDocument.Parse(autoRun.Out);
        var warnings = json.RootElement.GetProperty("jobs")[0].GetProperty("warnings");
        Assert.Contains(warnings.EnumerateArray(), w => w.GetProperty("code").GetString() == "concat-reencoded");
    }

    [Fact]
    public async Task ConfigTools_ShowsResolvedToolPathsAndHonoursSettingsOverride()
    {
        SkipWithoutFFmpeg();

        var defaultRun = await RunAsync("config", "tools");
        Assert.Equal(0, defaultRun.ExitCode);
        Assert.Contains("ffmpeg", defaultRun.Out, StringComparison.Ordinal);
        Assert.Contains("ffprobe", defaultRun.Out, StringComparison.Ordinal);

        SettingsStore.Save(Paths.SettingsFile, FluxSettings.Default with
        {
            Tools = new ToolPathOverrides(Tools.FFmpeg!.Path, Tools.FFprobe!.Path),
        });

        var configuredRun = await RunAsync("config", "tools");
        Assert.Equal(0, configuredRun.ExitCode);
        Assert.Contains("(Configured)", configuredRun.Out, StringComparison.Ordinal);
    }
}
