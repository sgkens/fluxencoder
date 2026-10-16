using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;

namespace FluxEncoder.Core.Tests.Hardware;

public sealed class CpuStrategyTests
{
    private readonly CpuStrategy _strategy = new();

    [Theory]
    [InlineData(VideoCodec.H264, "libx264")]
    [InlineData(VideoCodec.Hevc, "libx265")]
    [InlineData(VideoCodec.Av1, "libaom-av1")]
    public void TryGetVideoEncoder_MapsCodecToSoftwareEncoder(VideoCodec codec, string expected)
    {
        Assert.True(_strategy.TryGetVideoEncoder(codec, out var encoder));
        Assert.Equal(expected, encoder);
    }

    [Fact]
    public void TryGetVideoEncoder_CoversEveryCodec()
    {
        foreach (var codec in Enum.GetValues<VideoCodec>())
        {
            Assert.True(_strategy.TryGetVideoEncoder(codec, out _), $"No CPU encoder for {codec}.");
        }
    }

    [Fact]
    public void TryGetVideoEncoder_UndefinedCodec_ReturnsFalse()
    {
        Assert.False(_strategy.TryGetVideoEncoder((VideoCodec)999, out var encoder));
        Assert.Null(encoder);
    }

    [Fact]
    public void Cpu_UsesSoftwareDecoding()
    {
        Assert.Equal(HardwareVendor.Cpu, _strategy.Vendor);
        Assert.Empty(_strategy.DecodeArguments);
    }
}
