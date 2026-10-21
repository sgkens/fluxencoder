using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Automatic re-runs of a failed job. Only transient failures (network input, stall timeout) are retried;
/// an FFmpeg error on a local file fails the same way every time, so retrying it only wastes time.
/// </summary>
/// <remarks>
/// The default is <see cref="None"/>. The delay doubles after each attempt, capped at <see cref="MaxDelay"/>.
/// </remarks>
public sealed record RetryPolicy
{
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

    public RetryPolicy(int maxAttempts, TimeSpan initialDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialDelay, TimeSpan.Zero);

        MaxAttempts = maxAttempts;
        InitialDelay = initialDelay;
    }

    public static RetryPolicy None { get; } = new(1, TimeSpan.Zero);

    /// <summary>
    /// Total attempts, including the first. One means no retry.
    /// </summary>
    public int MaxAttempts { get; }

    public TimeSpan InitialDelay { get; }

    /// <param name="attempt">The attempt that just failed, starting at 1.</param>
    public bool ShouldRetry(JobFailure failure, int attempt)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        return failure.IsTransient && attempt < MaxAttempts;
    }

    /// <param name="attempt">The attempt that just failed, starting at 1.</param>
    public TimeSpan GetDelay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var ticks = InitialDelay.Ticks * Math.Pow(2, Math.Min(attempt - 1, 30));
        return ticks >= MaxDelay.Ticks ? MaxDelay : TimeSpan.FromTicks((long)ticks);
    }
}
