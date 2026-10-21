using System.Diagnostics.CodeAnalysis;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Maps codec requests to the FFmpeg encoder and decode arguments of one vendor.
/// </summary>
/// <remarks>
/// This is the single hardware model. Quality controls (TSKE006.4), the strategy registry
/// and capability probing (TSKE006.1, TSKE006.2), and VVC (TSKE006.6) extend this contract.
/// </remarks>
public interface IHardwareStrategy
{
    HardwareVendor Vendor { get; }

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
    /// Encoder options (name without the leading dash, value) that realise <paramref name="quality"/> as a
    /// constant-quality encode. The planner adds stream specifiers.
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality);

    /// <summary>
    /// Encoder options for a target-bit-rate encode at <paramref name="quality"/>: the speed preset without
    /// the constant-quality setting, which a bit-rate target replaces.
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality);

    /// <summary>
    /// Whether the encoder for <paramref name="codec"/> supports FFmpeg's <c>-pass</c>/<c>-passlogfile</c>.
    /// </summary>
    bool SupportsTwoPass(VideoCodec codec);
}
