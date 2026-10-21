using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Whether one vendor's encoder for one codec works on this machine, established by a test encode rather than by
/// the encoder appearing in <c>ffmpeg -encoders</c> (TSKE006.2).
/// </summary>
/// <param name="SupportsHighBitDepth">A 10-bit test encode also succeeded.</param>
/// <param name="Failure">Why the encoder is unavailable, classified from FFmpeg's error output.</param>
public sealed record EncoderProbe(
    HardwareVendor Vendor,
    VideoCodec Codec,
    string Encoder,
    EncoderStatus Status,
    bool SupportsHighBitDepth = false,
    JobFailure? Failure = null)
{
    public bool IsAvailable => Status == EncoderStatus.Available;
}
