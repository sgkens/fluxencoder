namespace FluxEncoder.Core.Media;

public sealed record SubtitleStream : MediaStream
{
    private static readonly HashSet<string> BitmapCodecs =
        new(["hdmv_pgs_subtitle", "dvd_subtitle", "dvb_subtitle", "xsub"], StringComparer.OrdinalIgnoreCase);

    public override MediaStreamKind Kind => MediaStreamKind.Subtitle;

    /// <summary>
    /// Image-based subtitles (PGS, VobSub, DVB) cannot be converted to text formats without OCR,
    /// which constrains the output container (TSKE005.2).
    /// </summary>
    public bool IsBitmap => CodecName is not null && BitmapCodecs.Contains(CodecName);

    public bool IsForced => Disposition.HasFlag(StreamDisposition.Forced);
}
