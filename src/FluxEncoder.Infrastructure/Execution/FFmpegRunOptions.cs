namespace FluxEncoder.Infrastructure.Execution;

public sealed record FFmpegRunOptions
{
    public static FFmpegRunOptions Default { get; } = new();

    /// <summary>
    /// How long FFmpeg gets to finalize the output after <c>q</c> before its process tree is killed. Finalizing
    /// can rewrite an MP4 index (<c>+faststart</c>), so this is generous.
    /// </summary>
    public TimeSpan GraceTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Stop FFmpeg when it reports no advance in frames, time, or size for this long, which catches a hung
    /// network input or a wedged hardware encoder. <see langword="null"/> disables the check.
    /// </summary>
    public TimeSpan? StallTimeout
    {
        get;
        init
        {
            if (value is { } stall)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stall, TimeSpan.Zero);
            }

            field = value;
        }
    } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Stop FFmpeg after this much wall-clock time (TSKE010.6); the run fails as a timeout. <see langword="null"/> means
    /// no limit.
    /// </summary>
    public TimeSpan? MaxRunTime
    {
        get;
        init
        {
            if (value is { } limit)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero);
            }

            field = value;
        }
    }

    /// <summary>
    /// Characters of stderr kept for diagnostics (the tail).
    /// </summary>
    public int MaxErrorChars
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1024);
            field = value;
        }
    } = 64 * 1024;
}
