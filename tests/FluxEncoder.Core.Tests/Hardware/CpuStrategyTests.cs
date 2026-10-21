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
    [InlineData(VideoCodec.Vvc, "libvvenc")]
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

    [Fact]
    public void SvtAv1_IsUsedWhenRequestedWithItsOwnScale()
    {
        var svt = new CpuStrategy(useSvtAv1: true);

        Assert.True(svt.TryGetVideoEncoder(VideoCodec.Av1, out var encoder));
        Assert.Equal("libsvtav1", encoder);
        Assert.Equal([new("preset", "8"), new("crf", "30")], svt.GetQualityOptions(VideoCodec.Av1, QualityPreset.Balanced));
        Assert.False(svt.SupportsTwoPass(VideoCodec.Av1));
        Assert.True(new CpuStrategy().SupportsTwoPass(VideoCodec.Av1));
    }

    [Fact]
    public void PixelFormats_KeepH264At8Bit()
    {
        Assert.Equal("yuv420p", _strategy.GetPixelFormat(VideoCodec.H264, highBitDepth: true));
        Assert.Equal("yuv420p10le", _strategy.GetPixelFormat(VideoCodec.Hevc, highBitDepth: true));
        Assert.Equal("yuv420p", _strategy.GetPixelFormat(VideoCodec.Av1, highBitDepth: false));
    }

    [Theory]
    [InlineData(VideoCodec.H264)]
    [InlineData(VideoCodec.Hevc)]
    [InlineData(VideoCodec.Av1)]
    [InlineData(VideoCodec.Vvc)]
    public void EveryCodecAndPreset_HasQualityOptions(VideoCodec codec)
    {
        foreach (var preset in Enum.GetValues<QualityPreset>())
        {
            Assert.NotEmpty(_strategy.GetQualityOptions(codec, preset));
            Assert.DoesNotContain(_strategy.GetBitRateOptions(codec, preset), o => o.Key is "crf" or "qp");
        }
    }
}
