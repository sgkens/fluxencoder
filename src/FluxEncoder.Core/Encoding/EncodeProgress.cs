namespace FluxEncoder.Core.Encoding;

/// <summary>
/// One FFmpeg progress report. Every value is optional because FFmpeg reports <c>N/A</c> until it knows it,
/// and audio-only jobs have no frame count.
/// </summary>
public sealed record EncodeProgress
{
    public long? Frame { get; init; }

    public double? FramesPerSecond { get; init; }

    public double? BitRateKbps { get; init; }

    public long? TotalSizeBytes { get; init; }

    /// <summary>
    /// Output timestamp reached, which is input time processed for an untrimmed job. Feeds
    /// <see cref="InputTimeline.Fraction"/>.
    /// </summary>
    public TimeSpan? ProcessedTime { get; init; }

    /// <summary>
    /// Processing speed relative to real time (2.0 = twice as fast as playback).
    /// </summary>
    public double? Speed { get; init; }

    public long? DuplicatedFrames { get; init; }

    public long? DroppedFrames { get; init; }

    /// <summary>
    /// The last report of the run (<c>progress=end</c>).
    /// </summary>
    public bool IsFinal { get; init; }
}
