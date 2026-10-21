using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Shared shape of the GPU strategies: an encoder per codec and system-memory input frames. Frames are uploaded by
/// the encoder itself, so plans need no <c>hwupload</c> filter and software filters keep working.
/// </summary>
/// <remarks>
/// Hardware decoding uses <c>-hwaccel auto</c> without an output format: FFmpeg picks an available decoder, copies
/// frames back to system memory, and silently falls back to software decoding when none fits.
/// </remarks>
public abstract class GpuStrategy : IHardwareStrategy
{
    private static readonly string[] AutoDecode = ["-hwaccel", "auto"];

    public abstract HardwareVendor Vendor { get; }

    public abstract string DisplayName { get; }

    public IReadOnlyList<string> DecodeArguments => AutoDecode;

    protected abstract string Suffix { get; }

    protected virtual bool HasEncoder(VideoCodec codec) => codec is VideoCodec.H264 or VideoCodec.Hevc or VideoCodec.Av1;

    public bool TryGetVideoEncoder(VideoCodec codec, [NotNullWhen(true)] out string? encoder)
    {
        encoder = HasEncoder(codec)
            ? codec switch
            {
                VideoCodec.H264 => "h264_" + Suffix,
                VideoCodec.Hevc => "hevc_" + Suffix,
                VideoCodec.Av1 => "av1_" + Suffix,
                _ => null,
            }
            : null;

        return encoder is not null;
    }

    public abstract IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality);

    public abstract IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality);

    /// <summary>
    /// GPU encoders analyse ahead internally (NVENC multipass, AMF pre-analysis) rather than through FFmpeg's
    /// <c>-pass</c>, so FFmpeg two-pass is never offered.
    /// </summary>
    public bool SupportsTwoPass(VideoCodec codec) => false;

    /// <summary>
    /// GPU encoders take NV12 for 8-bit and P010 for 10-bit. H.264 is never encoded 10-bit on GPUs. Whether a
    /// particular device accepts P010 is settled by the probe.
    /// </summary>
    public string? GetPixelFormat(VideoCodec codec, bool highBitDepth) => (codec, highBitDepth) switch
    {
        (VideoCodec.H264, true) => null,
        (_, true) => "p010le",
        _ => "nv12",
    };

    protected static int Level(QualityPreset quality, int fast, int balanced, int high) => quality switch
    {
        QualityPreset.Fast => fast,
        QualityPreset.Balanced => balanced,
        QualityPreset.High => high,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, null),
    };

    /// <summary>
    /// Constant-quality value per codec on a CRF-like scale: HEVC and AV1 reach a given visual quality at higher
    /// values than H.264.
    /// </summary>
    protected static string QualityValue(VideoCodec codec, QualityPreset quality) => (codec switch
    {
        VideoCodec.H264 => Level(quality, 25, 22, 19),
        VideoCodec.Hevc => Level(quality, 28, 25, 22),
        _ => Level(quality, 34, 30, 26),
    }).ToString(CultureInfo.InvariantCulture);
}
