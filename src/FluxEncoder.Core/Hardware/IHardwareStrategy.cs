using System.Diagnostics.CodeAnalysis;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// How one vendor's encoders are driven: codec mapping, decode flags, pixel formats, and quality controls.
/// </summary>
/// <remarks>
/// A strategy describes what an encoder accepts in principle. Whether it works on this machine is established by a
/// device-backed probe (<see cref="EncoderProbe"/>, TSKE006.2), and which strategy a job uses is decided by
/// <see cref="HardwareSelector"/> (TSKE006.3). Option names are returned without the leading dash; the planner adds
/// stream specifiers.
/// </remarks>
public interface IHardwareStrategy
{
    HardwareVendor Vendor { get; }

    /// <summary>
    /// Name for messages, such as "AMD AMF".
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Arguments placed before <c>-i</c> to select hardware decoding. Empty for software decoding.
    /// </summary>
    IReadOnlyList<string> DecodeArguments { get; }

    /// <summary>
    /// Resolves the FFmpeg encoder name for <paramref name="codec"/>.
    /// </summary>
    /// <returns><see langword="false"/> when this vendor has no encoder for the codec.</returns>
    bool TryGetVideoEncoder(VideoCodec codec, [NotNullWhen(true)] out string? encoder);

    /// <summary>
    /// Options that realise <paramref name="quality"/> as a constant-quality encode.
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality);

    /// <summary>
    /// Options for a target-bit-rate encode at <paramref name="quality"/>: the speed preset and rate-control
    /// mode, without the constant-quality setting. The planner adds the bit rate itself.
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality);

    /// <summary>
    /// Whether the encoder for <paramref name="codec"/> supports FFmpeg's <c>-pass</c>/<c>-passlogfile</c>.
    /// </summary>
    bool SupportsTwoPass(VideoCodec codec);

    /// <summary>
    /// The input pixel format the encoder takes for 8-bit or high-bit-depth output, or <see langword="null"/> when it
    /// cannot encode that depth at all.
    /// </summary>
    string? GetPixelFormat(VideoCodec codec, bool highBitDepth);
}
