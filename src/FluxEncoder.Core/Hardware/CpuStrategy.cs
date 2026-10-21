using System.Diagnostics.CodeAnalysis;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Software encoding through FFmpeg's CPU encoders: libx264, libx265, libaom-av1 or libsvtav1, and libvvenc.
/// </summary>
/// <remarks>
/// x264/x265 use CRF with a speed preset; the CRF values follow each encoder's usual visually transparent range
/// (x265's scale sits about 3 above x264's). AV1 prefers libsvtav1 when the build has it, being several times faster
/// than libaom-av1 at similar quality. VVC (libvvenc) is experimental and capability-gated (TSKE006.6).
/// </remarks>
public sealed class CpuStrategy(bool useSvtAv1 = false) : IHardwareStrategy
{
    public HardwareVendor Vendor => HardwareVendor.Cpu;

    public string DisplayName => "CPU";

    public IReadOnlyList<string> DecodeArguments => [];

    public bool UsesSvtAv1 => useSvtAv1;

    public bool TryGetVideoEncoder(VideoCodec codec, [NotNullWhen(true)] out string? encoder)
    {
        encoder = codec switch
        {
            VideoCodec.H264 => "libx264",
            VideoCodec.Hevc => "libx265",
            VideoCodec.Av1 => useSvtAv1 ? "libsvtav1" : "libaom-av1",
            VideoCodec.Vvc => "libvvenc",
            _ => null,
        };

        return encoder is not null;
    }

    public IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality) => (codec, quality, useSvtAv1) switch
    {
        (VideoCodec.H264, QualityPreset.Fast, _) => Options.Of(("preset", "veryfast"), ("crf", "23")),
        (VideoCodec.H264, QualityPreset.Balanced, _) => Options.Of(("preset", "medium"), ("crf", "20")),
        (VideoCodec.H264, QualityPreset.High, _) => Options.Of(("preset", "slow"), ("crf", "18")),
        (VideoCodec.Hevc, QualityPreset.Fast, _) => Options.Of(("preset", "veryfast"), ("crf", "26")),
        (VideoCodec.Hevc, QualityPreset.Balanced, _) => Options.Of(("preset", "medium"), ("crf", "23")),
        (VideoCodec.Hevc, QualityPreset.High, _) => Options.Of(("preset", "slow"), ("crf", "20")),
        (VideoCodec.Av1, QualityPreset.Fast, true) => Options.Of(("preset", "10"), ("crf", "35")),
        (VideoCodec.Av1, QualityPreset.Balanced, true) => Options.Of(("preset", "8"), ("crf", "30")),
        (VideoCodec.Av1, QualityPreset.High, true) => Options.Of(("preset", "5"), ("crf", "26")),
        (VideoCodec.Av1, QualityPreset.Fast, false) => Options.Of(("cpu-used", "8"), ("crf", "34"), ("b", "0")),
        (VideoCodec.Av1, QualityPreset.Balanced, false) => Options.Of(("cpu-used", "6"), ("crf", "30"), ("b", "0")),
        (VideoCodec.Av1, QualityPreset.High, false) => Options.Of(("cpu-used", "4"), ("crf", "26"), ("b", "0")),
        (VideoCodec.Vvc, QualityPreset.Fast, _) => Options.Of(("preset", "faster"), ("qp", "32")),
        (VideoCodec.Vvc, QualityPreset.Balanced, _) => Options.Of(("preset", "medium"), ("qp", "28")),
        (VideoCodec.Vvc, QualityPreset.High, _) => Options.Of(("preset", "slower"), ("qp", "24")),
        _ => throw new ArgumentOutOfRangeException(nameof(codec), (codec, quality), null),
    };

    public IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality) =>
        [.. GetQualityOptions(codec, quality).Where(option => option.Key is not ("crf" or "qp" or "b"))];

    /// <remarks>
    /// libx265 is excluded: its two-pass statistics are passed through <c>-x265-params</c>, whose colon-separated
    /// syntax cannot carry a Windows path. libsvtav1 and libvvenc do not support FFmpeg's <c>-pass</c>.
    /// </remarks>
    public bool SupportsTwoPass(VideoCodec codec) => codec is VideoCodec.H264 || (codec is VideoCodec.Av1 && !useSvtAv1);

    /// <remarks>
    /// H.264 is written 8-bit for player compatibility even though libx264 can encode 10-bit.
    /// </remarks>
    public string? GetPixelFormat(VideoCodec codec, bool highBitDepth) => (codec, highBitDepth) switch
    {
        (VideoCodec.H264, _) => "yuv420p",
        (_, true) => "yuv420p10le",
        _ => "yuv420p",
    };
}
