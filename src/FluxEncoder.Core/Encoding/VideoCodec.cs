namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Target video codec requested for an encode, independent of the encoder implementation
/// that produces it. Hardware strategies map each value to a concrete FFmpeg encoder.
/// </summary>
public enum VideoCodec
{
    H264,
    Hevc,
    Av1,

    /// <summary>
    /// H.266/VVC. Experimental: available only when the FFmpeg build includes <c>libvvenc</c>, and few players
    /// decode it yet.
    /// </summary>
    Vvc,
}
