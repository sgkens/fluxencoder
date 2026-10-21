using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// AMD AMF (Windows). <c>-quality speed|balanced|quality</c>; constant quality via CQP with equal I/P(/B) QPs,
/// which every AMF generation supports (QVBR needs newer drivers). Bit-rate targets use peak-constrained VBR.
/// </summary>
/// <remarks>
/// Verified on a Radeon RX 580 (Polaris) with FFmpeg 7.1.1: H.264 and HEVC encode; AV1 is not available on that
/// generation, and its driver refuses 10-bit. Both are detected by the device probe, not assumed.
/// </remarks>
public sealed class AmfStrategy : GpuStrategy
{
    public override HardwareVendor Vendor => HardwareVendor.Amd;

    public override string DisplayName => "AMD AMF";

    protected override string Suffix => "amf";

    public override IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality)
    {
        var qp = QualityValue(codec, quality);
        return codec == VideoCodec.H264
            ? Options.Of(("quality", Speed(quality)), ("rc", "cqp"), ("qp_i", qp), ("qp_p", qp), ("qp_b", qp))
            : Options.Of(("quality", Speed(quality)), ("rc", "cqp"), ("qp_i", qp), ("qp_p", qp));
    }

    public override IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality) =>
        Options.Of(("quality", Speed(quality)), ("rc", "vbr_peak"));

    private static string Speed(QualityPreset quality) => quality switch
    {
        QualityPreset.Fast => "speed",
        QualityPreset.Balanced => "balanced",
        _ => "quality",
    };
}
