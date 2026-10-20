namespace FluxEncoder.Core.Media;

/// <summary>
/// Stream type as reported by FFprobe <c>codec_type</c>.
/// </summary>
public enum MediaStreamKind
{
    Video,
    Audio,
    Subtitle,
    Attachment,
    Data,
}
