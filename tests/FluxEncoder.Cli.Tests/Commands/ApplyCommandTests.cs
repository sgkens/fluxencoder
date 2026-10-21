using System.Text.Json;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Storage;
using FluxEncoder.Infrastructure.Tooling;
using Microsoft.Data.Sqlite;

namespace FluxEncoder.Cli.Tests.Commands;

/// <summary>
/// <c>apply</c> through the real command surface: validation, the Fluxfile trust boundary, and real runs in dependency
/// order.
/// </summary>
public sealed class ApplyCommandTests : IAsyncLifetime
{
    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-apply-" + Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(PathOf("raw"));
        if (Tools.IsComplete)
        {
            var output = await ProcessCapture.RunAsync(
                Tools.FFmpeg!.Path,
                ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=1", "-f", "lavfi", "-i", "sine=duration=1", "-c:v", "libx264", "-c:a", "aac", PathOf("raw", "clip.mkv")],
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

    private Task<Run> ApplyAsync(string fluxfile, params string[] args) => ApplyWithVariablesAsync(new Dictionary<string, string>(), fluxfile, args);

    private async Task<Run> ApplyWithVariablesAsync(IReadOnlyDictionary<string, string> variables, string fluxfile, params string[] args)
    {
        var path = PathOf("Fluxfile.yaml");
        await File.WriteAllTextAsync(path, fluxfile, TestContext.Current.CancellationToken);

        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error, name => variables.GetValueOrDefault(name), ErrorIsTerminal: false) { Paths = Paths };

        var exitCode = await CliApplication.RunAsync(["apply", path, .. args], host, TestContext.Current.CancellationToken);
        return new Run(exitCode, output.ToString(), error.ToString());
    }

    private static void SkipWithoutFFmpeg() => Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");

    private const string Chain = """
        version: 1
        defaults:
          quality: fast
        jobs:
          web:
            input: "@master"
            output: out/web.mp4
            audio-codec: aac
          master:
            input: raw/clip.mkv
            output: out/master.mkv
        """;

    [Fact]
    public async Task Check_ValidatesWithoutRunningAnything()
    {
        var run = await ApplyAsync(Chain, "--check", "--ffmpeg", "/nonexistent/ffmpeg");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("is valid: 2 jobs, run in this order: master, web.", run.Out, StringComparison.Ordinal);
        Assert.False(Directory.Exists(PathOf("out")));
    }

    [Fact]
    public async Task Check_Json_DescribesTheJobs()
    {
        var run = await ApplyAsync(Chain, "--check", "--json");

        using var document = JsonDocument.Parse(run.Out);
        var jobs = document.RootElement.GetProperty("jobs");
        Assert.Equal(["master", "web"], jobs.EnumerateArray().Select(j => j.GetProperty("name").GetString()));
        Assert.Equal("@master", jobs[1].GetProperty("inputs")[0].GetString());
        Assert.Equal(PathOf("out", "web.mp4"), jobs[1].GetProperty("output").GetString());
        Assert.Equal("master", jobs[1].GetProperty("needs")[0].GetString());
    }

    [Fact]
    public async Task InvalidFluxfile_ReportsEveryProblemWithPositions()
    {
        var run = await ApplyAsync("""
            version: 1
            defaults:
              qualty: high
            jobs:
              a:
                input: raw/clip.mkv
                output: ../escape.mkv
                ffmpeg: /bin/sh
              b:
                input: "@a"
                needs: [zz]
            """);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("has 4 problems", run.Error, StringComparison.Ordinal);
        Assert.Contains("Fluxfile.yaml:3:3: Unknown encode option 'qualty'; did you mean 'quality'?", run.Error, StringComparison.Ordinal);
        Assert.Contains("Fluxfile.yaml:7:13: 'output' resolves to", run.Error, StringComparison.Ordinal);
        Assert.Contains("Fluxfile.yaml:8:5: 'ffmpeg' cannot be set in a Fluxfile", run.Error, StringComparison.Ordinal);
        Assert.Contains("needs 'zz'", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("version: 1\njobs: {}\n", "'jobs' is empty")]
    [InlineData("version: 1\n", "'jobs' is required")]
    [InlineData("", "The file is empty")]
    [InlineData("version: 1\njobs:\n  a:\n    input: raw/clip.mkv\n    codec: h265\n", "job 'a': Argument 'h265' not recognized")]
    [InlineData("version: 1\njobs:\n  a:\n    input: raw/clip.mkv\n    ffmpeg-arg: -y\n", "'ffmpeg-arg' cannot be set in a Fluxfile")]
    [InlineData("version: 1\njobs:\n  a:\n    input: raw/clip.mkv\n    preset: nope\n", "Unknown preset 'nope'")]
    public async Task EmptyOrInvalidJobs_NeverSucceed(string fluxfile, string expected)
    {
        foreach (var mode in new[] { "--check", "--dry-run" })
        {
            var run = await ApplyAsync(fluxfile, mode);

            Assert.Equal(2, run.ExitCode);
            Assert.Contains(expected, run.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OptionLikeValues_StayValues()
    {
        var run = await ApplyAsync("version: 1\njobs:\n  a:\n    input: raw/clip.mkv\n    start: \"--ffmpeg=/bin/sh\"\n", "--check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("'--ffmpeg=/bin/sh' is not a valid time", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionLikeInputs_AreResolvedAsFiles()
    {
        var run = await ApplyAsync("version: 1\njobs:\n  a:\n    input: [\"--ffmpeg-arg=-y\"]\n    output: out/\n", "--check", "--json");

        using var document = JsonDocument.Parse(run.Out);
        Assert.Equal(PathOf("--ffmpeg-arg=-y"), document.RootElement.GetProperty("jobs")[0].GetProperty("inputs")[0].GetString());
    }

    [Fact]
    public async Task Variables_ComeFromVarAndFluxPrefixedEnvironmentOnly()
    {
        const string fluxfile = "version: 1\njobs:\n  a:\n    input: ${FLUX_SRC}/clip.mkv\n    output: out/${NAME}.mkv\n";
        var environment = new Dictionary<string, string> { ["FLUX_SRC"] = "raw", ["NAME"] = "from-env", ["SECRET_TOKEN"] = "x" };

        var run = await ApplyWithVariablesAsync(environment, fluxfile, "--check", "--json", "--var", "NAME=given");
        using var document = JsonDocument.Parse(run.Out);
        var job = document.RootElement.GetProperty("jobs")[0];
        Assert.Equal(PathOf("raw", "clip.mkv"), job.GetProperty("inputs")[0].GetString());
        Assert.Equal(PathOf("out", "given.mkv"), job.GetProperty("output").GetString());

        var unprefixed = await ApplyWithVariablesAsync(environment, fluxfile, "--check");
        Assert.Equal(2, unprefixed.ExitCode);
        Assert.Contains("Variable 'NAME' is not set", unprefixed.Error, StringComparison.Ordinal);

        var token = await ApplyWithVariablesAsync(environment, "version: 1\njobs:\n  a:\n    input: https://example/${SECRET_TOKEN}\n", "--check");
        Assert.Equal(2, token.ExitCode);
    }

    [Fact]
    public async Task InvalidVar_IsUsageError()
    {
        var run = await ApplyAsync(Chain, "--check", "--var", "no-equals");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--var takes NAME=VALUE", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutsideOutputs_NeedAllowOutside()
    {
        const string fluxfile = "version: 1\njobs:\n  a:\n    input: raw/clip.mkv\n    output: ../elsewhere.mkv\n";

        Assert.Equal(2, (await ApplyAsync(fluxfile, "--check")).ExitCode);
        Assert.Equal(0, (await ApplyAsync(fluxfile, "--check", "--allow-outside")).ExitCode);
    }

    [Fact]
    public async Task MissingFluxfile_IsUsageError()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error, _ => null, ErrorIsTerminal: false) { Paths = Paths };

        var exitCode = await CliApplication.RunAsync(["apply", PathOf("nope.yaml")], host, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("The file does not exist", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownJobSelection_IsUsageError()
    {
        var run = await ApplyAsync(Chain, "--check", "--job", "nope");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("There is no job 'nope' (jobs: web, master).", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_FollowsDependenciesAndIsSafeToRepeat()
    {
        SkipWithoutFFmpeg();

        var first = await ApplyAsync(Chain);

        Assert.True(first.ExitCode == 0, first.Error);
        Assert.True(new FileInfo(PathOf("out", "master.mkv")).Length > 0);
        Assert.True(new FileInfo(PathOf("out", "web.mp4")).Length > 0);
        Assert.True(first.Error.IndexOf("master:", StringComparison.Ordinal) < first.Error.IndexOf("web:", StringComparison.Ordinal));
        Assert.DoesNotContain("Could not remove", first.Error, StringComparison.Ordinal);

        var written = File.GetLastWriteTimeUtc(PathOf("out", "web.mp4"));
        var second = await ApplyAsync(Chain);

        Assert.Equal(0, second.ExitCode);
        Assert.Contains("0 succeeded, 2 skipped, 0 failed", second.Out, StringComparison.Ordinal);
        Assert.Equal(written, File.GetLastWriteTimeUtc(PathOf("out", "web.mp4")));

        var master = File.GetLastWriteTimeUtc(PathOf("out", "master.mkv"));
        var forced = await ApplyAsync(Chain, "--overwrite", "--job", "master");
        Assert.Equal(0, forced.ExitCode);
        Assert.NotEqual(master, File.GetLastWriteTimeUtc(PathOf("out", "master.mkv")));
        Assert.Equal(written, File.GetLastWriteTimeUtc(PathOf("out", "web.mp4")));
    }

    [Fact]
    public async Task DryRun_PlansJobsWhoseInputsDoNotExistYetLater()
    {
        SkipWithoutFFmpeg();

        var run = await ApplyAsync(Chain, "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("its input is written by 'master'", run.Out, StringComparison.Ordinal);
        Assert.False(Directory.Exists(PathOf("out")));
    }

    [Fact]
    public async Task FailedJob_BlocksOnlyWhatNeedsIt()
    {
        SkipWithoutFFmpeg();

        var run = await ApplyAsync("""
            version: 1
            jobs:
              broken:
                input: raw/missing.mkv
                output: out/broken.mkv
              after:
                input: "@broken"
                output: out/after.mkv
              independent:
                input: raw/clip.mkv
                output: out/independent.mkv
                quality: fast
            """);

        Assert.Equal(10, run.ExitCode);
        Assert.Contains("Job 'after' did not start: it needs 'broken', which did not finish.", run.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(PathOf("out", "independent.mkv")));
        Assert.False(File.Exists(PathOf("out", "after.mkv")));
    }
}
