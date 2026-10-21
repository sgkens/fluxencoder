namespace FluxEncoder.Core.Media;

/// <summary>
/// Stream disposition flags, one per FFprobe <c>disposition</c> key.
/// </summary>
/// <remarks>
/// <see cref="AttachedPicture"/> marks cover art: a video stream holding one still image, which is
/// distinct from an <see cref="AttachmentStream"/> such as an embedded font.
/// </remarks>
[Flags]
public enum StreamDisposition
{
    None = 0,
    Default = 1 << 0,
    Dub = 1 << 1,
    Original = 1 << 2,
    Comment = 1 << 3,
    Lyrics = 1 << 4,
    Karaoke = 1 << 5,
    Forced = 1 << 6,
    HearingImpaired = 1 << 7,
    VisualImpaired = 1 << 8,
    CleanEffects = 1 << 9,
    AttachedPicture = 1 << 10,
    TimedThumbnails = 1 << 11,
    NonDiegetic = 1 << 12,
    Captions = 1 << 13,
    Descriptions = 1 << 14,
    Metadata = 1 << 15,
    Dependent = 1 << 16,
    StillImage = 1 << 17,
}
