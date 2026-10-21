namespace FluxEncoder.Core.Media;

/// <summary>
/// One stream of a probed input. Each <see cref="MediaStreamKind"/> has its own derived record so that
/// kind-specific properties exist only where they apply.
/// </summary>
/// <remarks>
/// A <see langword="null"/> property means the container does not declare that value; zero is never used
/// as an "unknown" marker.
/// </remarks>
public abstract record MediaStream
{
    private protected MediaStream()
    {
    }

    public abstract MediaStreamKind Kind { get; }

    /// <summary>
    /// Zero-based stream index within the input, as used in FFmpeg <c>-map 0:N</c>.
    /// </summary>
    public required int Index
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// FFmpeg codec name such as <c>h264</c> or <c>aac</c>; <see langword="null"/> when FFmpeg cannot identify it.
    /// </summary>
    public string? CodecName { get; init; }

    public LanguageTag? Language { get; init; }

    public StreamDisposition Disposition { get; init; }

    public string? Title { get; init; }

    public long? BitRate { get; init; }

    public TimeSpan? Duration { get; init; }

    public IReadOnlyDictionary<string, string> Tags { get; init; } = MediaTags.Empty;

    public bool IsDefault => Disposition.HasFlag(StreamDisposition.Default);
}
