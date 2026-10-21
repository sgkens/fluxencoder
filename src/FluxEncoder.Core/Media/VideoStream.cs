namespace FluxEncoder.Core.Media;

public sealed record VideoStream : MediaStream
{
    public override MediaStreamKind Kind => MediaStreamKind.Video;

    public int? Width { get; init; }

    public int? Height { get; init; }

    /// <summary>
    /// FFprobe <c>pix_fmt</c>, for example <c>yuv420p10le</c>.
    /// </summary>
    public string? PixelFormat { get; init; }

    public int? BitDepth { get; init; }

    /// <summary>
    /// Mean frame rate over the stream (<c>avg_frame_rate</c>).
    /// </summary>
    public Rational? AverageFrameRate { get; init; }

    /// <summary>
    /// Lowest rate that represents every timestamp (<c>r_frame_rate</c>). Differs from
    /// <see cref="AverageFrameRate"/> for variable-frame-rate content.
    /// </summary>
    public Rational? BaseFrameRate { get; init; }

    public Rational? SampleAspectRatio { get; init; }

    /// <summary>
    /// Display-matrix rotation in degrees, counter-clockwise, as FFprobe reports it in side data.
    /// </summary>
    public int? Rotation { get; init; }

    public ColorInfo Color { get; init; } = ColorInfo.Unspecified;

    public HdrInfo Hdr { get; init; } = HdrInfo.None;

    /// <summary>
    /// Cover art or another single still image rather than motion video.
    /// </summary>
    public bool IsAttachedPicture => Disposition.HasFlag(StreamDisposition.AttachedPicture);

    public HdrFormat HdrFormats => Hdr.GetFormats(Color);
}
