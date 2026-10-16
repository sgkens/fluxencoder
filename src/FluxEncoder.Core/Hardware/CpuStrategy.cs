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
}
