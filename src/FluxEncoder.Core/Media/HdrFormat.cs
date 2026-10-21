namespace FluxEncoder.Core.Media;

/// <summary>
/// High-dynamic-range formats detected on a video stream. Formats combine: a Dolby Vision stream
/// commonly also carries an HDR10 base layer.
/// </summary>
[Flags]
public enum HdrFormat
{
    None = 0,

    /// <summary>
    /// PQ transfer (<c>smpte2084</c>), with or without static metadata.
    /// </summary>
    Hdr10 = 1 << 0,

    /// <summary>
    /// SMPTE ST 2094-40 dynamic metadata.
    /// </summary>
    Hdr10Plus = 1 << 1,

    /// <summary>
    /// HLG transfer (<c>arib-std-b67</c>).
    /// </summary>
    Hlg = 1 << 2,

    DolbyVision = 1 << 3,
}
