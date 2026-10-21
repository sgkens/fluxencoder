using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;

namespace FluxEncoder.Core.Tests.Hardware;

public sealed class GpuStrategyTests
{
    private static string Render(IEnumerable<KeyValuePair<string, string>> options) =>
        string.Join(' ', options.Select(o => $"-{o.Key} {o.Value}"));

    [Theory]
    [InlineData(HardwareVendor.Nvidia, VideoCodec.H264, "h264_nvenc")]
    [InlineData(HardwareVendor.Nvidia, VideoCodec.Av1, "av1_nvenc")]
    [InlineData(HardwareVendor.Amd, VideoCodec.Hevc, "hevc_amf")]
    [InlineData(HardwareVendor.Amd, VideoCodec.Av1, "av1_amf")]
    [InlineData(HardwareVendor.Intel, VideoCodec.Hevc, "hevc_qsv")]
    [InlineData(HardwareVendor.Apple, VideoCodec.H264, "h264_videotoolbox")]
    public void Encoders_FollowVendorNaming(HardwareVendor vendor, VideoCodec codec, string expected)
    {
        var strategy = HardwareRegistry.For(PlanningCapabilities()).For(vendor);

        Assert.True(strategy.TryGetVideoEncoder(codec, out var encoder));
        Assert.Equal(expected, encoder);
    }

    [Theory]
    [InlineData(HardwareVendor.Apple, VideoCodec.Av1)]
    [InlineData(HardwareVendor.Nvidia, VideoCodec.Vvc)]
    [InlineData(HardwareVendor.Amd, VideoCodec.Vvc)]
    public void MissingEncoders_AreReported(HardwareVendor vendor, VideoCodec codec)
    {
        Assert.False(HardwareRegistry.For(PlanningCapabilities()).For(vendor).TryGetVideoEncoder(codec, out _));
    }

    [Fact]
    public void QualityOptions_UseEachEncodersOwnControls()
    {
        Assert.Equal("-preset p5 -tune hq -rc vbr -cq 25 -b 0", Render(new NvencStrategy().GetQualityOptions(VideoCodec.Hevc, QualityPreset.Balanced)));
        Assert.Equal("-quality speed -rc cqp -qp_i 25 -qp_p 25 -qp_b 25", Render(new AmfStrategy().GetQualityOptions(VideoCodec.H264, QualityPreset.Fast)));
        Assert.Equal("-quality quality -rc cqp -qp_i 22 -qp_p 22", Render(new AmfStrategy().GetQualityOptions(VideoCodec.Hevc, QualityPreset.High)));
        Assert.Equal("-preset veryslow -global_quality 26", Render(new QsvStrategy().GetQualityOptions(VideoCodec.Av1, QualityPreset.High)));
        Assert.Equal("-q 65", Render(new VideoToolboxStrategy().GetQualityOptions(VideoCodec.Hevc, QualityPreset.Balanced)));
    }

    [Fact]
    public void BitRateOptions_DropConstantQualityControls()
    {
        Assert.Equal("-preset p7 -tune hq -rc vbr", Render(new NvencStrategy().GetBitRateOptions(VideoCodec.H264, QualityPreset.High)));
        Assert.Equal("-quality balanced -rc vbr_peak", Render(new AmfStrategy().GetBitRateOptions(VideoCodec.Hevc, QualityPreset.Balanced)));
    }

    [Fact]
    public void PixelFormats_AreNv12AndP010_NoTenBitH264()
    {
        var amf = new AmfStrategy();

        Assert.Equal("nv12", amf.GetPixelFormat(VideoCodec.H264, false));
        Assert.Null(amf.GetPixelFormat(VideoCodec.H264, true));
        Assert.Equal("p010le", amf.GetPixelFormat(VideoCodec.Hevc, true));
    }

    [Fact]
    public void Gpus_NeverOfferFfmpegTwoPassAndDecodeWithAutoHwaccel()
    {
        foreach (var gpu in HardwareRegistry.For(PlanningCapabilities()).Gpus)
        {
            Assert.False(gpu.SupportsTwoPass(VideoCodec.H264));
            Assert.Equal(["-hwaccel", "auto"], gpu.DecodeArguments);
        }
    }

    [Fact]
    public void EightBitOnly_HidesHighBitDepthOnly()
    {
        var limited = new EightBitOnlyStrategy(new AmfStrategy());

        Assert.Null(limited.GetPixelFormat(VideoCodec.Hevc, true));
        Assert.Equal("nv12", limited.GetPixelFormat(VideoCodec.Hevc, false));
        Assert.Equal(HardwareVendor.Amd, limited.Vendor);
        Assert.True(limited.TryGetVideoEncoder(VideoCodec.Hevc, out var encoder));
        Assert.Equal("hevc_amf", encoder);
    }

    [Fact]
    public void Registry_OrdersDiscreteBeforeIntegratedAndPicksSvtWhenPresent()
    {
        var registry = HardwareRegistry.For(PlanningCapabilities("libsvtav1"));

        Assert.Equal([HardwareVendor.Nvidia, HardwareVendor.Amd, HardwareVendor.Intel, HardwareVendor.Apple], registry.Gpus.Select(g => g.Vendor));
        Assert.True(registry.Cpu.TryGetVideoEncoder(VideoCodec.Av1, out var av1));
        Assert.Equal("libsvtav1", av1);
        Assert.Same(registry.Cpu, registry.For(HardwareVendor.Cpu));
        Assert.Throws<ArgumentException>(() => new HardwareRegistry(new CpuStrategy(), [new AmfStrategy(), new AmfStrategy()]));
    }

    private static FluxEncoder.Core.Tooling.FFmpegCapabilities PlanningCapabilities(params string[] encoders) =>
        Planning.PlannerFixture.Capabilities(encoders.Length == 0 ? ["libx264"] : encoders);
}
