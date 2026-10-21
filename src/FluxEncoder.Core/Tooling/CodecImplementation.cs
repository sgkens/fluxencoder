using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Tooling;

/// <summary>
/// One FFmpeg encoder or decoder, such as <c>libx265</c> or <c>hevc_nvenc</c>.
/// </summary>
/// <remarks>
/// A listed encoder is only compiled in. Whether a hardware encoder works on this machine needs a
/// device-backed test encode (TSKE006.2).
/// </remarks>
public sealed record CodecImplementation(string Name, MediaStreamKind Kind, string Description)
{
    /// <summary>
    /// The codec this implementation produces or reads, from the <c>(codec hevc)</c> suffix; for native
    /// implementations FFmpeg omits it because the name is the codec.
    /// </summary>
    public string? CodecName { get; init; }

    /// <summary>
    /// FFmpeg marks the implementation experimental; using it requires <c>-strict experimental</c>.
    /// </summary>
    public bool IsExperimental { get; init; }

    /// <summary>
    /// <see cref="CodecName"/>, falling back to <see cref="Name"/>.
    /// </summary>
    public string Codec => CodecName ?? Name;
}
