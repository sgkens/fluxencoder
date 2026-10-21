using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Whether an input has a known end. Finite files and unbounded inputs (live streams, capture devices,
/// pipes without a declared duration) differ in progress reporting, in what stopping means, and in which
/// features are available, so the runner and UI branch on this rather than on a missing duration.
/// </summary>
public abstract record InputTimeline
{
    private protected InputTimeline()
    {
    }

    /// <summary>
    /// An input with a known duration. Stopping it early is a cancellation and the partial output follows
    /// <see cref="PartialOutputPolicy"/>.
    /// </summary>
    public sealed record Finite : InputTimeline
    {
        public Finite(TimeSpan duration)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
            Duration = duration;
        }

        public TimeSpan Duration { get; }

        public override bool SupportsPercentage => true;

        public override bool StopCompletesJob => false;

        public override bool SupportsMultiPass => true;

        public override bool SupportsJobRetry => true;

        public override double? Fraction(TimeSpan processed) =>
            Math.Clamp(processed / Duration, 0.0, 1.0);
    }

    /// <summary>
    /// An input with no known end. It runs until the source ends or the user stops it, and stopping is the
    /// normal way to finish: FFmpeg is asked to finalize the file, which is kept and reported as succeeded
    /// with <see cref="Results.WarningCodes.StoppedByUser"/>.
    /// </summary>
    public sealed record Unbounded : InputTimeline
    {
        public override bool SupportsPercentage => false;

        public override bool StopCompletesJob => true;

        public override bool SupportsMultiPass => false;

        public override bool SupportsJobRetry => false;

        public override double? Fraction(TimeSpan processed) => null;
    }

    /// <summary>
    /// Whether progress can be shown as a percentage with an ETA. When <see langword="false"/>, show elapsed
    /// time, processed media time, speed, and size instead.
    /// </summary>
    public abstract bool SupportsPercentage { get; }

    /// <summary>
    /// Whether a user stop (Ctrl+C, FFmpeg <c>q</c>) finalizes and keeps the output as a successful result
    /// instead of cancelling the job.
    /// </summary>
    public abstract bool StopCompletesJob { get; }

    /// <summary>
    /// Two-pass encoding needs to read the whole input twice, which an unbounded input cannot do (TSKE005.6).
    /// </summary>
    public abstract bool SupportsMultiPass { get; }

    /// <summary>
    /// Whether <see cref="RetryPolicy"/> may re-run the job. Re-running a live recording would restart it into
    /// a new file and lose the gap; network sources reconnect inside FFmpeg instead.
    /// </summary>
    public abstract bool SupportsJobRetry { get; }

    /// <summary>
    /// Progress in [0, 1] for <paramref name="processed"/> media time, or <see langword="null"/> when unknowable.
    /// </summary>
    public abstract double? Fraction(TimeSpan processed);

    public static InputTimeline From(MediaInfo media)
    {
        ArgumentNullException.ThrowIfNull(media);

        return media is { HasKnownDuration: true, Duration: { } duration } ? new Finite(duration) : new Unbounded();
    }
}
