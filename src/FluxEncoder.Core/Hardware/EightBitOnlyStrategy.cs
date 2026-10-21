using System.Diagnostics.CodeAnalysis;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// A strategy whose device failed the 10-bit probe: identical to the wrapped strategy except that it offers no
/// high-bit-depth pixel format, so the planner writes 8-bit and warns.
/// </summary>
public sealed class EightBitOnlyStrategy(IHardwareStrategy inner) : IHardwareStrategy
{
    public IHardwareStrategy Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public HardwareVendor Vendor => Inner.Vendor;

    public string DisplayName => Inner.DisplayName;

    public IReadOnlyList<string> DecodeArguments => Inner.DecodeArguments;

    public bool TryGetVideoEncoder(VideoCodec codec, [NotNullWhen(true)] out string? encoder) => Inner.TryGetVideoEncoder(codec, out encoder);

    public IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality) => Inner.GetQualityOptions(codec, quality);

    public IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality) => Inner.GetBitRateOptions(codec, quality);

    public bool SupportsTwoPass(VideoCodec codec) => Inner.SupportsTwoPass(codec);

    public string? GetPixelFormat(VideoCodec codec, bool highBitDepth) => highBitDepth ? null : Inner.GetPixelFormat(codec, false);
}
