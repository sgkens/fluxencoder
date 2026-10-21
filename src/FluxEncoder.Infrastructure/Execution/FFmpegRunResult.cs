using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Infrastructure.Execution;

/// <param name="ExitCode">FFmpeg's exit code; after a forced kill this is whatever the OS reports.</param>
/// <param name="Termination">Why the run ended.</param>
/// <param name="Killed">FFmpeg ignored <c>q</c> for the whole grace period and its process tree was killed.</param>
/// <param name="StandardError">The tail of FFmpeg's stderr.</param>
/// <param name="LastProgress">The last progress report received, if any.</param>
/// <param name="Elapsed">Wall-clock time from start to exit.</param>
public sealed record FFmpegRunResult(
    int ExitCode,
    RunTermination Termination,
    bool Killed,
    string StandardError,
    EncodeProgress? LastProgress,
    TimeSpan Elapsed)
{
    public bool Succeeded =>
        Termination == RunTermination.Exited && ExitCode == 0 && !FFmpegFailureClassifier.IndicatesFailure(StandardError);

    /// <summary>
    /// FFmpeg honoured a stop request and finalized its output. For an unbounded input this is a normal finish.
    /// </summary>
    public bool StoppedCleanly => Termination == RunTermination.Stopped && !Killed && ExitCode == 0;

    /// <summary>
    /// The classified failure, or <see langword="null"/> when FFmpeg succeeded.
    /// </summary>
    public JobFailure? ToFailure(TimeSpan? stallTimeout = null) => Termination switch
    {
        RunTermination.Exited when Succeeded => null,
        RunTermination.Exited => FFmpegFailureClassifier.Classify(ExitCode, StandardError),
        RunTermination.Stopped => new JobFailure(FailureCategory.Cancelled, "Cancelled.") { Detail = Killed ? "FFmpeg did not stop within the grace period and was killed." : null },
        _ => new JobFailure(
            FailureCategory.Timeout,
            stallTimeout is { } stall
                ? $"FFmpeg made no progress for {stall.TotalSeconds:0} s and was stopped."
                : "FFmpeg stopped making progress and was stopped.")
        {
            Detail = StandardError.Trim(),
        },
    };
}
