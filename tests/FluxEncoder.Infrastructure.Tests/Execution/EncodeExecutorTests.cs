using System.Collections.Concurrent;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Execution;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Tests.Execution;

/// <summary>
/// Runs real FFmpeg through the executor. Skipped when FFmpeg is not installed.
/// </summary>
public sealed class EncodeExecutorTests : IDisposable
{
    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);
    private static readonly InputTimeline TwoSeconds = new InputTimeline.Finite(TimeSpan.FromSeconds(2));
    private static readonly string[] ShortSource = ["-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=2"];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-exec-" + Guid.NewGuid().ToString("N"));

    public EncodeExecutorTests() => Directory.CreateDirectory(_directory);

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

    private string PathOf(string name) => Path.Combine(_directory, name);

    private string Temporary(string final) => OutputPaths.Temporary(final, "t1");

    private static EncodingJob Job(string output, JobPolicy? policy = null) => new()
    {
        InputPath = "lavfi",
        OutputPath = output,
        Video = new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Fast),
        Audio = new AudioTarget.StreamCopy(),
        Policy = policy ?? JobPolicy.Default,
    };

    private OutputPlan SinglePass(string final, string[]? source = null, JobPolicy? policy = null, string codec = "libx264") =>
        new(Job(final, policy), [new FFmpegInvocation([.. source ?? ShortSource, "-c:v", codec, "-n", Temporary(final)])])
        {
            TemporaryOutputPath = Temporary(final),
        };

    private sealed class Collector : IProgress<JobProgress>
    {
        public ConcurrentQueue<JobProgress> Reports { get; } = new();

        public void Report(JobProgress value) => Reports.Enqueue(value);
    }

    [Fact]
    public async Task Success_MovesTemporaryIntoPlace()
    {
        var final = PathOf("out.mkv");
        var collector = new Collector();

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, SinglePass(final), TwoSeconds, collector, cancellationToken: Token);

        Assert.Equal(JobOutcome.Succeeded, result.Outcome);
        Assert.Equal(final, result.OutputPath);
        Assert.True(new FileInfo(final).Length > 0);
        Assert.False(File.Exists(Temporary(final)));
        Assert.Equal(1, result.Attempts);
        Assert.True(result.Elapsed > TimeSpan.Zero);
        Assert.InRange(collector.Reports.Last().Fraction!.Value, 0.9, 1.0);
        Assert.All(collector.Reports, r => Assert.InRange(r.Fraction!.Value, 0.0, 1.0));
    }

    [Fact]
    public async Task MissingOutputDirectory_IsCreatedWithoutCleanupWarnings()
    {
        var final = Path.Combine(_directory, "new", "nested", "out.mkv");

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, SinglePass(final), TwoSeconds, cancellationToken: Token);

        Assert.Equal(JobOutcome.Succeeded, result.Outcome);
        Assert.True(File.Exists(final));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ExistingFinal_IsNotReplacedUnderNever()
    {
        var final = PathOf("taken.mkv");
        await File.WriteAllTextAsync(final, "original", Token);

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, SinglePass(final), TwoSeconds, cancellationToken: Token);

        Assert.Equal(FailureCategory.OutputExists, result.Failure?.Category);
        Assert.Equal("original", await File.ReadAllTextAsync(final, Token));
        Assert.False(File.Exists(Temporary(final)));
    }

    [Fact]
    public async Task ExistingFinal_IsReplacedUnderOverwrite()
    {
        var final = PathOf("replace.mkv");
        await File.WriteAllTextAsync(final, "original", Token);

        var result = await EncodeExecutor.ExecuteAsync(
            FFmpeg, SinglePass(final, policy: JobPolicy.Default with { Overwrite = OverwritePolicy.Overwrite }), TwoSeconds, cancellationToken: Token);

        Assert.Equal(JobOutcome.Succeeded, result.Outcome);
        Assert.NotEqual("original", await File.ReadAllTextAsync(final, Token));
    }

    [Fact]
    public async Task FFmpegFailure_IsClassifiedAndCleansUp()
    {
        var final = PathOf("fail.mkv");

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, SinglePass(final, codec: "libdoesnotexist"), TwoSeconds, cancellationToken: Token);

        Assert.Equal(FailureCategory.Unsupported, result.Failure?.Category);
        Assert.Equal(ExitCode.Unsupported, result.ExitCode);
        Assert.False(File.Exists(final));
        Assert.False(File.Exists(Temporary(final)));
    }

    [Theory]
    [InlineData(PartialOutputPolicy.Delete)]
    [InlineData(PartialOutputPolicy.Keep)]
    public async Task CancelledFiniteEncode_FollowsPartialOutputPolicy(PartialOutputPolicy partial)
    {
        var final = PathOf($"cancel-{partial}.mkv");
        var plan = SinglePass(final, ["-re", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=30"], JobPolicy.Default with { PartialOutput = partial });
        var collector = new Collector();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var running = EncodeExecutor.ExecuteAsync(FFmpeg, plan, new InputTimeline.Finite(TimeSpan.FromSeconds(30)), collector, cancellationToken: cts.Token);
        while (collector.Reports.IsEmpty)
        {
            await Task.Delay(50, Token);
        }

        await cts.CancelAsync();
        var result = await running;

        Assert.Equal(FailureCategory.Cancelled, result.Failure?.Category);
        Assert.Equal(ExitCode.Cancelled, result.ExitCode);
        Assert.False(File.Exists(final));
        Assert.Equal(partial == PartialOutputPolicy.Keep, File.Exists(Temporary(final)));
        Assert.Equal(partial == PartialOutputPolicy.Keep, result.Warnings.Any(w => w.Code == WarningCodes.PartialOutputKept));
    }

    [Fact]
    public async Task StoppedUnboundedRecording_Succeeds()
    {
        var final = PathOf("recording.mkv");
        var plan = SinglePass(final, ["-re", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25"]);
        var collector = new Collector();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var running = EncodeExecutor.ExecuteAsync(FFmpeg, plan, new InputTimeline.Unbounded(), collector, cancellationToken: cts.Token);
        while (collector.Reports.IsEmpty)
        {
            await Task.Delay(50, Token);
        }

        await cts.CancelAsync();
        var result = await running;

        Assert.Equal(JobOutcome.Succeeded, result.Outcome);
        Assert.Equal(WarningCodes.StoppedByUser, Assert.Single(result.Warnings).Code);
        Assert.True(new FileInfo(final).Length > 0);
        Assert.All(collector.Reports, r => Assert.Null(r.Fraction));
    }

    [Fact]
    public async Task TwoPass_ReportsBothPassesAndDeletesScratch()
    {
        var final = PathOf("twopass.mp4");
        var scratch = PathOf("scratch");
        Directory.CreateDirectory(scratch);
        var log = Path.Combine(scratch, "pass");
        var nullOutput = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        var plan = new OutputPlan(Job(final),
        [
            new FFmpegInvocation([.. ShortSource, "-c:v", "libx264", "-b:v", "200k", "-pass", "1", "-passlogfile", log, "-an", "-f", "null", nullOutput]),
            new FFmpegInvocation([.. ShortSource, "-c:v", "libx264", "-b:v", "200k", "-pass", "2", "-passlogfile", log, "-n", Temporary(final)]),
        ])
        {
            TemporaryOutputPath = Temporary(final),
            ScratchDirectory = scratch,
        };
        var collector = new Collector();

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, plan, TwoSeconds, collector, cancellationToken: Token);

        Assert.Equal(JobOutcome.Succeeded, result.Outcome);
        Assert.Equal([1, 2], collector.Reports.Select(r => r.Pass).Distinct());
        Assert.All(collector.Reports.Where(r => r.Pass == 1), r => Assert.InRange(r.Fraction!.Value, 0.0, 0.5));
        Assert.All(collector.Reports.Where(r => r.Pass == 2), r => Assert.InRange(r.Fraction!.Value, 0.5, 1.0));
        Assert.False(Directory.Exists(scratch));
    }

    [Fact]
    public async Task TransientFailure_IsRetriedWithinPolicy()
    {
        var final = PathOf("net.mkv");
        var policy = JobPolicy.Default with { Retry = new RetryPolicy(3, TimeSpan.Zero) };
        var plan = SinglePass(final, ["-i", "http://127.0.0.1:9/stream.ts"], policy);

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, plan, TwoSeconds, cancellationToken: Token);

        Assert.Equal(FailureCategory.NetworkError, result.Failure?.Category);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task UnboundedInput_IsNotRetried()
    {
        var final = PathOf("live-net.mkv");
        var policy = JobPolicy.Default with { Retry = new RetryPolicy(3, TimeSpan.Zero) };

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, SinglePass(final, ["-i", "http://127.0.0.1:9/live.ts"], policy), new InputTimeline.Unbounded(), cancellationToken: Token);

        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task MissingOutput_FailsValidation()
    {
        var final = PathOf("validated.mkv");
        var plan = new OutputPlan(Job(final), [new FFmpegInvocation([.. ShortSource, "-c:v", "libx264", "-n", PathOf("elsewhere.mkv")])])
        {
            TemporaryOutputPath = Temporary(final),
        };

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, plan, TwoSeconds, cancellationToken: Token);

        Assert.Equal(FailureCategory.OutputValidationFailed, result.Failure?.Category);
        Assert.Equal(ExitCode.OutputValidationFailed, result.ExitCode);
    }

    [Fact]
    public async Task DirectOutput_SkipsMoveAndValidation()
    {
        var final = PathOf("direct.mkv");
        var plan = new OutputPlan(Job(final), [new FFmpegInvocation([.. ShortSource, "-c:v", "libx264", "-n", final])]);

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, plan, TwoSeconds, cancellationToken: Token);

        Assert.Equal(JobOutcome.Succeeded, result.Outcome);
        Assert.True(File.Exists(final));
    }

    [Fact]
    public async Task MissingFFmpeg_IsDependencyFailure()
    {
        var final = PathOf("x.mkv");

        var result = await EncodeExecutor.ExecuteAsync(PathOf("no-ffmpeg"), SinglePass(final), TwoSeconds, cancellationToken: Token);

        Assert.Equal(FailureCategory.DependencyMissing, result.Failure?.Category);
    }

    [Fact]
    public async Task OutputReachingSizeLimit_IsNotPromoted()
    {
        var final = PathOf("limited.mkv");
        const long Limit = 1024 * 1024;
        var plan = new OutputPlan(Job(final),
        [
            new FFmpegInvocation(["-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=25:duration=20", "-c:v", "libx264", "-preset", "ultrafast", "-qp", "0", "-fs", "1048576", "-n", Temporary(final)]),
        ])
        {
            TemporaryOutputPath = Temporary(final),
            MaxOutputBytes = Limit,
        };

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, plan, new InputTimeline.Finite(TimeSpan.FromSeconds(20)), cancellationToken: Token);

        Assert.Equal(FailureCategory.PolicyDenied, result.Failure?.Category);
        Assert.Contains("size limit", result.Failure!.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(final));
        Assert.False(File.Exists(Temporary(final)));
    }

    [Fact]
    public async Task RunTimeLimit_StopsTheEncode()
    {
        var final = PathOf("forever.mkv");
        var plan = SinglePass(final, ["-re", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25"]);
        var options = new FFmpegRunOptions { MaxRunTime = TimeSpan.FromSeconds(2), GraceTimeout = TimeSpan.FromSeconds(5) };
        var started = DateTime.UtcNow;

        var result = await EncodeExecutor.ExecuteAsync(FFmpeg, plan, new InputTimeline.Finite(TimeSpan.FromHours(1)), options: options, cancellationToken: Token);

        Assert.Equal(FailureCategory.Timeout, result.Failure?.Category);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20));
        Assert.False(File.Exists(final));
    }
}
