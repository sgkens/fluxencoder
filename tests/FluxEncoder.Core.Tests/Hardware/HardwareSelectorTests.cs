using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Tests.Hardware;

public sealed class HardwareSelectorTests
{
    private static readonly HardwareRegistry Registry = new(new CpuStrategy(), [new NvencStrategy(), new AmfStrategy(), new QsvStrategy(), new VideoToolboxStrategy()]);

    private static readonly MediaInfo Sdr8 = new()
    {
        Source = "in.mkv",
        Duration = TimeSpan.FromMinutes(1),
        Streams = [new VideoStream { Index = 0, CodecName = "h264", BitDepth = 8 }],
    };

    private static readonly MediaInfo Sdr10 = Sdr8 with { Streams = [new VideoStream { Index = 0, CodecName = "hevc", BitDepth = 10 }] };

    private static readonly MediaInfo Hdr = Sdr8 with
    {
        Streams = [new VideoStream { Index = 0, CodecName = "hevc", BitDepth = 10, Color = new ColorInfo { Transfer = "smpte2084" } }],
    };

    private static EncodingJob Job(VideoCodec codec = VideoCodec.Hevc, QualityPreset quality = QualityPreset.Balanced, HardwareVendor? hardware = null, bool fallback = false) => new()
    {
        InputPath = "in.mkv",
        OutputPath = "out.mkv",
        Video = new VideoTarget.Encode(codec, quality),
        Audio = new AudioTarget.StreamCopy(),
        Hardware = hardware,
        HardwareFallback = fallback,
    };

    private static EncoderProbe Works(HardwareVendor vendor, VideoCodec codec = VideoCodec.Hevc, bool tenBit = true) =>
        new(vendor, codec, "enc", EncoderStatus.Available, tenBit);

    private static EncoderProbe Fails(HardwareVendor vendor, VideoCodec codec = VideoCodec.Hevc) =>
        new(vendor, codec, "enc", EncoderStatus.Unavailable, Failure: new JobFailure(FailureCategory.HardwareUnavailable, "Cannot load nvcuda.dll") { Detail = "stderr" });

    private static HardwareSelection Selected(EncodingJob job, MediaInfo media, params EncoderProbe[] probes)
    {
        var result = HardwareSelector.Select(job, media, Registry, probes);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        return result.Value;
    }

    [Fact]
    public void Auto_PicksFirstWorkingGpuInOrder()
    {
        var selection = Selected(Job(), Sdr8, Fails(HardwareVendor.Nvidia), Works(HardwareVendor.Amd), Works(HardwareVendor.Intel));

        Assert.Equal(HardwareVendor.Amd, selection.Strategy.Vendor);
        Assert.True(selection.FallbackAllowed);
        Assert.Empty(selection.Warnings);
    }

    [Fact]
    public void Auto_SkipsGpuWithout10BitForDeepSource()
    {
        var selection = Selected(Job(), Sdr10, Works(HardwareVendor.Amd, tenBit: false), Works(HardwareVendor.Intel, tenBit: true));

        Assert.Equal(HardwareVendor.Intel, selection.Strategy.Vendor);
    }

    [Fact]
    public void Auto_8BitH264IgnoresTenBitSupport()
    {
        var selection = Selected(Job(VideoCodec.H264), Sdr10, Works(HardwareVendor.Amd, VideoCodec.H264, tenBit: false));

        Assert.Equal(HardwareVendor.Amd, selection.Strategy.Vendor);
    }

    [Fact]
    public void Auto_NoWorkingGpu_IsSilentCpu()
    {
        var selection = Selected(Job(), Sdr8, Fails(HardwareVendor.Nvidia), Fails(HardwareVendor.Amd));

        Assert.Equal(HardwareVendor.Cpu, selection.Strategy.Vendor);
        Assert.False(selection.FallbackAllowed);
        Assert.Empty(selection.Warnings);
    }

    [Theory]
    [InlineData(QualityPreset.High, false)]
    [InlineData(QualityPreset.Balanced, true)]
    public void Auto_PrefersCpuForHighQualityAndHdr(QualityPreset quality, bool hdr)
    {
        var media = hdr ? Hdr : Sdr8;

        Assert.Empty(HardwareSelector.StrategiesToProbe(Job(quality: quality), media, Registry, _ => true));
        Assert.Equal(HardwareVendor.Cpu, Selected(Job(quality: quality), media, Works(HardwareVendor.Amd)).Strategy.Vendor);
    }

    [Fact]
    public void Auto_PrefersCpuForTwoPass()
    {
        var job = Job(VideoCodec.H264) with { Video = new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Fast) { TargetBitRateKbps = 3000, TwoPass = true } };

        Assert.Equal(HardwareVendor.Cpu, Selected(job, Sdr8, Works(HardwareVendor.Amd, VideoCodec.H264)).Strategy.Vendor);
    }

    [Fact]
    public void StrategiesToProbe_OnlyListedEncodersAndOnlyForcedVendor()
    {
        var listed = new HashSet<string> { "hevc_amf", "hevc_nvenc" };

        Assert.Equal([HardwareVendor.Nvidia, HardwareVendor.Amd], HardwareSelector.StrategiesToProbe(Job(), Sdr8, Registry, listed.Contains).Select(s => s.Vendor));
        Assert.Equal([HardwareVendor.Amd], HardwareSelector.StrategiesToProbe(Job(hardware: HardwareVendor.Amd), Sdr8, Registry, listed.Contains).Select(s => s.Vendor));
        Assert.Empty(HardwareSelector.StrategiesToProbe(Job(hardware: HardwareVendor.Cpu), Sdr8, Registry, listed.Contains));
        Assert.Empty(HardwareSelector.StrategiesToProbe(Job(VideoCodec.Vvc), Sdr8, Registry, _ => true));
    }

    [Fact]
    public void Forced_WorkingVendorIsUsedWithFallbackOnlyIfRequested()
    {
        Assert.False(Selected(Job(hardware: HardwareVendor.Amd), Sdr8, Works(HardwareVendor.Amd)).FallbackAllowed);
        Assert.True(Selected(Job(hardware: HardwareVendor.Amd, fallback: true), Sdr8, Works(HardwareVendor.Amd)).FallbackAllowed);
    }

    [Fact]
    public void Forced_UnavailableVendorFailsWithReasonAndHint()
    {
        var result = HardwareSelector.Select(Job(hardware: HardwareVendor.Nvidia), Sdr8, Registry, [Fails(HardwareVendor.Nvidia)]);

        Assert.Equal(FailureCategory.HardwareUnavailable, result.Failure?.Category);
        Assert.Contains("NVIDIA NVENC cannot encode Hevc", result.Failure!.Message, StringComparison.Ordinal);
        Assert.Contains("--hardware-fallback", result.Failure.Message, StringComparison.Ordinal);
        Assert.Equal("stderr", result.Failure.Detail);
        Assert.Equal(ExitCode.Unsupported, result.Failure.ExitCode);
    }

    [Fact]
    public void Forced_NotListedVendorNamesTheMissingEncoder()
    {
        var result = HardwareSelector.Select(Job(hardware: HardwareVendor.Intel), Sdr8, Registry, []);

        Assert.Contains("no hevc_qsv encoder", result.Failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Forced_UnavailableWithFallback_UsesCpuAndWarns()
    {
        var selection = Selected(Job(hardware: HardwareVendor.Nvidia, fallback: true), Sdr8, Fails(HardwareVendor.Nvidia));

        Assert.Equal(HardwareVendor.Cpu, selection.Strategy.Vendor);
        Assert.Equal(WarningCodes.HardwareFallback, Assert.Single(selection.Warnings).Code);
    }

    [Fact]
    public void Forced_DeviceWithout10Bit_IsKeptButLimitedTo8Bit()
    {
        var selection = Selected(Job(hardware: HardwareVendor.Amd), Hdr, Works(HardwareVendor.Amd, tenBit: false));

        var limited = Assert.IsType<EightBitOnlyStrategy>(selection.Strategy);
        Assert.Equal(HardwareVendor.Amd, limited.Vendor);
    }

    [Fact]
    public void Vvc_IsCpuOnly()
    {
        Assert.Equal(HardwareVendor.Cpu, Selected(Job(VideoCodec.Vvc), Sdr8).Strategy.Vendor);
        Assert.Equal(FailureCategory.Unsupported, HardwareSelector.Select(Job(VideoCodec.Vvc, hardware: HardwareVendor.Amd), Sdr8, Registry, []).Failure?.Category);
    }

    [Fact]
    public void StreamCopy_NeedsNoHardware()
    {
        var job = Job() with { Video = new VideoTarget.StreamCopy(), Hardware = HardwareVendor.Nvidia };

        Assert.Equal(HardwareVendor.Cpu, Selected(job, Sdr8).Strategy.Vendor);
        Assert.Empty(HardwareSelector.StrategiesToProbe(job, Sdr8, Registry, _ => true));
    }
}
