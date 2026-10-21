namespace FluxEncoder.Core.Media;

/// <summary>
/// An embedded file, typically a font for styled subtitles in Matroska. Cover art is not an attachment:
/// it is a <see cref="VideoStream"/> with <see cref="StreamDisposition.AttachedPicture"/>.
/// </summary>
public sealed record AttachmentStream : MediaStream
{
    public override MediaStreamKind Kind => MediaStreamKind.Attachment;

    public string? FileName { get; init; }

    public string? MimeType { get; init; }
}
