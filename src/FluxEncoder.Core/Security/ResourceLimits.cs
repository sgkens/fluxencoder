namespace FluxEncoder.Core.Security;

/// <summary>
/// Ceilings for running FluxEncoder as a service, where inputs and requests may come from others (TSKE010.6).
/// Every limit is off by default.
/// </summary>
/// <remarks>
/// Telemetry and captured FFmpeg output are always bounded (a fixed sample count per run and the last 64 KB of
/// stderr), so they need no setting here.
/// </remarks>
public sealed record ResourceLimits
{
    public static ResourceLimits None { get; } = new();

    /// <summary>
    /// Stop an FFmpeg run that takes longer than this; it fails as a timeout.
    /// </summary>
    public TimeSpan? MaxRunTime
    {
        get;
        init
        {
            if (value is { } time)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(time, TimeSpan.Zero);
            }

            field = value;
        }
    }

    /// <summary>
    /// Refuse inputs larger than this many bytes, as reported by the probe.
    /// </summary>
    public long? MaxInputBytes
    {
        get;
        init
        {
            if (value is { } bytes)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
            }

            field = value;
        }
    }

    /// <summary>
    /// Stop writing at this many bytes (FFmpeg <c>-fs</c>); an output that reaches it is incomplete and fails
    /// validation rather than being promoted.
    /// </summary>
    public long? MaxOutputBytes
    {
        get;
        init
        {
            if (value is { } bytes)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(bytes, 1024L * 1024);
            }

            field = value;
        }
    }

    /// <summary>
    /// Threads FFmpeg may use for encoding and filtering.
    /// </summary>
    public int? Threads
    {
        get;
        init
        {
            if (value is { } threads)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(threads, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(threads, 256);
            }

            field = value;
        }
    }
}
