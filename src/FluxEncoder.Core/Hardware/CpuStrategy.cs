using System.Diagnostics.CodeAnalysis;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Software encoding through FFmpeg's CPU encoders.
/// </summary>
public sealed class CpuStrategy : IHardwareStrategy
{
    public HardwareVendor Vendor => HardwareVendor.Cpu;

    public IReadOnlyList<string> DecodeArguments => [];

    public bool TryGetVideoEncoder(VideoCodec codec, [NotNullWhen(true)] out string? encoder)
    {
        encoder = codec switch
        {
            VideoCodec.H264 => "libx264",
            VideoCodec.Hevc => "libx265",
            VideoCodec.Av1 => "libaom-av1",
            _ => null,
        };

        return encoder is not null;
    }

    /// <remarks>
    /// x264/x265 use CRF with a speed preset; the CRF values follow each encoder's usual visually-transparent
    /// range (x265's scale sits about 5 above x264's). libaom-av1 uses constant quality via <c>-crf</c> with
    /// <c>-b:v 0</c> and trades speed with <c>-cpu-used</c>. Per-encoder tuning is refined in TSKE006.4.
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality) => (codec, quality) switch
    {
        (VideoCodec.H264, QualityPreset.Fast) => Options(("preset", "veryfast"), ("crf", "23")),
        (VideoCodec.H264, QualityPreset.Balanced) => Options(("preset", "medium"), ("crf", "20")),
        (VideoCodec.H264, QualityPreset.High) => Options(("preset", "slow"), ("crf", "18")),
        (VideoCodec.Hevc, QualityPreset.Fast) => Options(("preset", "veryfast"), ("crf", "26")),
        (VideoCodec.Hevc, QualityPreset.Balanced) => Options(("preset", "medium"), ("crf", "23")),
        (VideoCodec.Hevc, QualityPreset.High) => Options(("preset", "slow"), ("crf", "20")),
        (VideoCodec.Av1, QualityPreset.Fast) => Options(("cpu-used", "8"), ("crf", "34"), ("b", "0")),
        (VideoCodec.Av1, QualityPreset.Balanced) => Options(("cpu-used", "6"), ("crf", "30"), ("b", "0")),
        (VideoCodec.Av1, QualityPreset.High) => Options(("cpu-used", "4"), ("crf", "26"), ("b", "0")),
        _ => throw new ArgumentOutOfRangeException(nameof(codec), (codec, quality), null),
    };

    public IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality) =>
        [.. GetQualityOptions(codec, quality).Where(option => option.Key is not ("crf" or "b"))];

    /// <remarks>
    /// libx265 is excluded: its two-pass statistics are passed through <c>-x265-params</c>, whose
    /// colon-separated syntax cannot carry a Windows path. Constant-quality HEVC needs no second pass.
    /// </remarks>
    public bool SupportsTwoPass(VideoCodec codec) => codec is VideoCodec.H264 or VideoCodec.Av1;

    private static KeyValuePair<string, string>[] Options(params (string Key, string Value)[] options) =>
        [.. options.Select(o => KeyValuePair.Create(o.Key, o.Value))];
}
