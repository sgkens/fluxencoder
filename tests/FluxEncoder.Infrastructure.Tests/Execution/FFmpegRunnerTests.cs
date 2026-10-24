using System.Collections.Concurrent;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Execution;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Tests.Execution;

/// <summary>
/// Runs the real FFmpeg. Skipped when FFmpeg is not installed.
/// </summary>
public sealed class FFmpegRunnerTests : IDisposable
{
    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-runner-" + Guid.NewGuid().ToString("N"));

    public FFmpegRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string FFmpeg
    {
        get
        {
            Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");
            return Tools.FFmpeg!.Path;
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Out(string name) => Path.Combine(_directory, name);

    /// <summary>
    /// Collects reports synchronously; <see cref="Progress{T}"/> would post them to the thread pool.
    /// </summary>
    private sealed class Collector : IProgress<EncodeProgress>
    {
        public ConcurrentQueue<EncodeProgress> Reports { get; } = new();

        public void Report(EncodeProgress value) => Reports.Enqueue(value);
    }

    [Fact]
    public async Task FiniteEncode_SucceedsWithProgress()
    {
        var collector = new Collector();
        var invocation = new FFmpegInvocation(
            ["-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=2", "-c:v", "libx264", "-n", Out("clip.mkv")]);

        var result = await FFmpegRunner.RunAsync(FFmpeg, invocation, collector, cancellationToken: Token);

        Assert.True(result.IsSuccess);
        var run = result.Value;
        Assert.True(run.Succeeded, run.StandardError);
        Assert.Null(run.ToFailure());
        Assert.Equal(RunTermination.Exited, run.Termination);
        Assert.True(run.LastProgress?.IsFinal);
        Assert.Equal(50, run.LastProgress?.Frame);
        Assert.NotEmpty(collector.Reports);
        Assert.True(new FileInfo(Out("clip.mkv")).Length > 0);
    }

    [Fact]
    public async Task UnknownEncoder_IsClassifiedUnsupported()
    {
        var invocation = new FFmpegInvocation(
            ["-f", "lavfi", "-i", "testsrc2=duration=1", "-c:v", "libdoesnotexist", "-n", Out("x.mkv")]);

        var result = await FFmpegRunner.RunAsync(FFmpeg, invocation, cancellationToken: Token);

        var run = result.Value!;
        Assert.False(run.Succeeded);
        var failure = run.ToFailure();
        Assert.Equal(FailureCategory.Unsupported, failure?.Category);
        Assert.Contains("libdoesnotexist", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingOutputWithNoOverwrite_IsOutputExists()
    {
        await File.WriteAllTextAsync(Out("taken.mkv"), "x", Token);
        var invocation = new FFmpegInvocation(["-f", "lavfi", "-i", "testsrc2=duration=1", "-n", Out("taken.mkv")]);

        var run = (await FFmpegRunner.RunAsync(FFmpeg, invocation, cancellationToken: Token)).Value!;

        Assert.Equal(FailureCategory.OutputExists, run.ToFailure()?.Category);
        Assert.Equal("x", await File.ReadAllTextAsync(Out("taken.mkv"), Token));
    }

    [Fact]
    public async Task Cancel_SendsQAndFinalizesOutput()
    {
        var collector = new Collector();
        var invocation = new FFmpegInvocation(
            ["-re", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25", "-c:v", "libx264", "-n", Out("live.mkv")]);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var running = FFmpegRunner.RunAsync(FFmpeg, invocation, collector, cancellationToken: cts.Token);
        while (collector.Reports.IsEmpty)
        {
            await Task.Delay(50, Token);
        }

        await cts.CancelAsync();
        var run = (await running).Value!;

        Assert.Equal(RunTermination.Stopped, run.Termination);
        Assert.True(run.StoppedCleanly, run.StandardError);
        Assert.False(run.Killed);
        Assert.Equal(FailureCategory.Cancelled, run.ToFailure()?.Category);

        // The stopped recording is a complete, readable file.
        var probe = await ProcessCapture.RunAsync(Tools.FFprobe!.Path, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", "-i", Out("live.mkv")], TimeSpan.FromSeconds(30), Token);
        Assert.True(probe.Succeeded, probe.StandardError);
        Assert.True(double.Parse(probe.StandardOutput.Trim(), System.Globalization.CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task Stall_StopsThenKillsAfterGrace()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses a POSIX FIFO to simulate an input that never delivers data.");
        var fifo = Out("never.fifo");
        var mkfifo = await ProcessCapture.RunAsync("mkfifo", [fifo], TimeSpan.FromSeconds(10), Token);
        Assert.True(mkfifo.Succeeded);

        var invocation = new FFmpegInvocation(["-i", fifo, "-c", "copy", "-n", Out("stalled.mkv")]);
        var options = new FFmpegRunOptions { StallTimeout = TimeSpan.FromSeconds(1), GraceTimeout = TimeSpan.FromSeconds(1) };

        var run = (await FFmpegRunner.RunAsync(FFmpeg, invocation, options: options, cancellationToken: Token)).Value!;

        Assert.Equal(RunTermination.Stalled, run.Termination);
        Assert.True(run.Killed);
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(15));
        Assert.Equal(FailureCategory.Timeout, run.ToFailure(options.StallTimeout)?.Category);
    }

    [Fact]
    public async Task MissingExecutable_IsDependencyFailure()
    {
        var result = await FFmpegRunner.RunAsync(Out("no-ffmpeg"), new FFmpegInvocation(["-version"]), cancellationToken: Token);

        Assert.Equal(FailureCategory.DependencyMissing, result.Failure?.Category);
    }

    [Fact]
    public async Task VerboseStderr_DoesNotDeadlock()
    {
        // -loglevel debug writes far more than a pipe buffer to stderr.
        var invocation = new FFmpegInvocation(
            ["-loglevel", "debug", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=25:duration=2", "-c:v", "libx264", "-n", Out("verbose.mkv")]);

        var run = (await FFmpegRunner.RunAsync(FFmpeg, invocation, options: new FFmpegRunOptions { MaxErrorChars = 4096 }, cancellationToken: Token)).Value!;

        Assert.True(run.Succeeded);
        Assert.True(run.StandardError.Length <= 4096);
    }

    private string WriteFakeChild(string name, string unixScriptBody)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Uses a POSIX shell script fake child.");
            return string.Empty;
        }

        var path = Out(name);
        File.WriteAllText(path, "#!/bin/sh\n" + unixScriptBody + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public async Task FakeChild_InterleavesProgressStdoutWithHeavyStderrWithoutDeadlock()
    {
        var fake = WriteFakeChild("interleaved.sh", """
            printf 'frame=10\nout_time_us=1000000\ntotal_size=1024\nprogress=continue\n'
            i=0
            while [ $i -lt 4000 ]; do
                printf '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n' >&2
                i=$((i + 1))
            done
            printf 'frame=25\nout_time_us=2000000\ntotal_size=4096\nprogress=end\n'
            printf 'TAIL_MARKER\n' >&2
            """);

        var collector = new Collector();
        var run = (await FFmpegRunner.RunAsync(
            fake,
            new FFmpegInvocation(["-i", "in.mkv", "out.mkv"]),
            collector,
            new FFmpegRunOptions { MaxErrorChars = 2048 },
            Token)).Value!;

        Assert.True(run.Succeeded);
        Assert.Equal(2, collector.Reports.Count);
        Assert.True(run.LastProgress!.IsFinal);
        Assert.Equal(25, run.LastProgress.Frame);
        Assert.True(run.StandardError.Length <= 2048);
        Assert.EndsWith("TAIL_MARKER\n", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FakeChild_ExitsEarlyBeforeProgressEnd_IsNotSucceeded()
    {
        var fake = WriteFakeChild("early-exit.sh", """
            printf 'frame=5\nout_time_us=500000\ntotal_size=512\nprogress=continue\n'
            printf 'Conversion failed!\n' >&2
            exit 1
            """);

        var run = (await FFmpegRunner.RunAsync(fake, new FFmpegInvocation(["-i", "in.mkv", "out.mkv"]), cancellationToken: Token)).Value!;

        Assert.False(run.Succeeded);
        Assert.Equal(1, run.ExitCode);
        Assert.Equal(RunTermination.Exited, run.Termination);
        Assert.False(run.LastProgress!.IsFinal);
        Assert.Equal(5, run.LastProgress.Frame);
        Assert.Contains("Conversion failed!", run.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FakeChild_IgnoresGracefulQuitOnCancellation_IsKilledAfterGracePeriod()
    {
        var fake = WriteFakeChild("ignore-quit.sh", """
            printf 'frame=1\nout_time_us=100000\ntotal_size=128\nprogress=continue\n'
            sleep 30
            """);

        var collector = new Collector();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var running = FFmpegRunner.RunAsync(
            fake,
            new FFmpegInvocation(["-i", "in.mkv", "out.mkv"]),
            collector,
            new FFmpegRunOptions { GraceTimeout = TimeSpan.FromMilliseconds(150) },
            cts.Token);

        while (collector.Reports.IsEmpty)
        {
            await Task.Delay(25, Token);
        }

        await cts.CancelAsync();
        var run = (await running).Value!;

        Assert.Equal(RunTermination.Stopped, run.Termination);
        Assert.True(run.Killed);
        Assert.False(run.StoppedCleanly);
        Assert.False(run.Succeeded);
    }
}
