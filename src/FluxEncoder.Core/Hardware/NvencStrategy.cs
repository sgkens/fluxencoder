using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// NVIDIA NVENC. Presets p1 (fastest) to p7 (best); constant quality via <c>-rc vbr -cq N -b:v 0</c>.
/// </summary>
public sealed class NvencStrategy : GpuStrategy
{
    public override HardwareVendor Vendor => HardwareVendor.Nvidia;

    public override string DisplayName => "NVIDIA NVENC";

    protected override string Suffix => "nvenc";

    public override IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality) =>
        Options.Of(("preset", Preset(quality)), ("tune", "hq"), ("rc", "vbr"), ("cq", QualityValue(codec, quality)), ("b", "0"));

    public override IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality) =>
        Options.Of(("preset", Preset(quality)), ("tune", "hq"), ("rc", "vbr"));

    private static string Preset(QualityPreset quality) => quality switch
    {
        QualityPreset.Fast => "p2",
        QualityPreset.Balanced => "p5",
        _ => "p7",
    };
}
