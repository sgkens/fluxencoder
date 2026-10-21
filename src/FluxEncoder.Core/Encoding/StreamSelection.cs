namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Which parts of the input reach the output. <see cref="Default"/> keeps everything a viewer would miss
/// (primary video, all audio and subtitles, chapters, metadata, cover art, fonts) and drops only data
/// streams, which rarely survive a container change.
/// </summary>
/// <remarks>
/// Selection is container-independent. The planner removes what the output container cannot hold and
/// reports each removal as a warning (TSKE005.2, TSKE005.3).
/// </remarks>
public sealed record StreamSelection
{
    public static StreamSelection Default { get; } = new();

    public VideoSelection Video
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null);
    } = VideoSelection.Primary;

    public TrackSelection Audio
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = new TrackSelection.All();

    public TrackSelection Subtitles
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = new TrackSelection.All();

    public bool Chapters { get; init; } = true;

    /// <summary>
    /// Container-level tags such as title and year. Per-stream language and title are always kept.
    /// </summary>
    public bool Metadata { get; init; } = true;

    /// <summary>
    /// Keep location and recording-device tags (<see cref="MetadataPrivacy"/>) along with the other container tags.
    /// Off by default: a converted file is often shared, and those tags tell where and on what it was recorded.
    /// </summary>
    public bool PrivateMetadata { get; init; }

    /// <summary>
    /// Attached pictures, always stream-copied, never passed through the video encoder.
    /// </summary>
    public bool CoverArt { get; init; } = true;

    /// <summary>
    /// Embedded files such as subtitle fonts. Dropping them breaks styled (ASS) subtitles.
    /// </summary>
    public bool Attachments { get; init; } = true;

    /// <summary>
    /// Timecode, telemetry, and other data streams. Off by default because many muxers reject them.
    /// </summary>
    public bool DataStreams { get; init; }
}
