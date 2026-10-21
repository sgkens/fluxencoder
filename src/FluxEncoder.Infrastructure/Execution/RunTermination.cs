namespace FluxEncoder.Infrastructure.Execution;

/// <summary>
/// Why an FFmpeg run ended.
/// </summary>
public enum RunTermination
{
    /// <summary>
    /// FFmpeg exited on its own; the exit code says whether it succeeded.
    /// </summary>
    Exited,

    /// <summary>
    /// The caller cancelled and FFmpeg was asked to stop.
    /// </summary>
    Stopped,

    /// <summary>
    /// FFmpeg made no progress within <see cref="FFmpegRunOptions.StallTimeout"/> and was stopped.
    /// </summary>
    Stalled,
}
