using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Intel Quick Sync. <c>-preset veryfast|medium|veryslow</c>; constant quality via ICQ (<c>-global_quality N</c>).
/// </summary>
public sealed class QsvStrategy : GpuStrategy
{
    public override HardwareVendor Vendor => HardwareVendor.Intel;

    public override string DisplayName => "Intel Quick Sync";

    protected override string Suffix => "qsv";

    public override IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality) =>
        Options.Of(("preset", Preset(quality)), ("global_quality", QualityValue(codec, quality)));

    public override IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality) =>
        Options.Of(("preset", Preset(quality)));

    private static string Preset(QualityPreset quality) => quality switch
    {
        QualityPreset.Fast => "veryfast",
        QualityPreset.Balanced => "medium",
        _ => "veryslow",
    };
}
