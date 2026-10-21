namespace FluxEncoder.Core.Media;

/// <summary>
/// The probed description of one input: container, streams, chapters, and metadata.
/// </summary>
/// <remarks>
/// <see cref="Duration"/> is <see langword="null"/> when the input does not declare one, as for live streams
/// and some pipes; such inputs cannot report percentage progress (TSKE002.6, TSKE007.4).
/// </remarks>
public sealed record MediaInfo
{
    /// <summary>
    /// The path or URL that was probed.
    /// </summary>
    public required string Source
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>
    /// Demuxer names from FFprobe <c>format_name</c>, which may list several aliases (<c>matroska,webm</c>).
    /// </summary>
    public IReadOnlyList<string> FormatNames { get; init; } = [];

    public TimeSpan? Duration { get; init; }

    public TimeSpan? StartTime { get; init; }

    public long? SizeBytes { get; init; }

    public long? BitRate { get; init; }

    public IReadOnlyList<MediaStream> Streams { get; init; } = [];

    public IReadOnlyList<MediaChapter> Chapters { get; init; } = [];

    public IReadOnlyDictionary<string, string> Tags { get; init; } = MediaTags.Empty;

    public bool HasKnownDuration => Duration > TimeSpan.Zero;

    public IEnumerable<VideoStream> VideoStreams => Streams.OfType<VideoStream>();

    public IEnumerable<AudioStream> AudioStreams => Streams.OfType<AudioStream>();

    public IEnumerable<SubtitleStream> SubtitleStreams => Streams.OfType<SubtitleStream>();

    public IEnumerable<AttachmentStream> Attachments => Streams.OfType<AttachmentStream>();

    /// <summary>
    /// Video streams that carry motion video, excluding cover art.
    /// </summary>
    public IEnumerable<VideoStream> MotionVideoStreams => VideoStreams.Where(s => !s.IsAttachedPicture);

    public IEnumerable<VideoStream> CoverArt => VideoStreams.Where(s => s.IsAttachedPicture);
}
