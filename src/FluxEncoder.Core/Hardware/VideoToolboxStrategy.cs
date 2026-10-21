using System.Globalization;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Apple VideoToolbox (macOS). Constant quality via <c>-q:v</c> (1–100, Apple silicon); no AV1 encoder.
/// </summary>
public sealed class VideoToolboxStrategy : GpuStrategy
{
    public override HardwareVendor Vendor => HardwareVendor.Apple;

    public override string DisplayName => "Apple VideoToolbox";

    protected override string Suffix => "videotoolbox";

    protected override bool HasEncoder(VideoCodec codec) => codec is VideoCodec.H264 or VideoCodec.Hevc;

    public override IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality) =>
        Options.Of(("q", Level(quality, 55, 65, 75).ToString(CultureInfo.InvariantCulture)));

    public override IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality) => [];
}
