using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Tests.Hardware;

/// <summary>
/// Device-backed probes (TSKE006.2, TSKE006.5) against real FFmpeg builds.
/// </summary>
public sealed class EncoderProberTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<FFmpegCapabilities> CapabilitiesAsync(string ffmpeg)
    {
        var result = await FFmpegCapabilityProbe.ProbeAsync(ffmpeg, cancellationToken: Token);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        return result.Value;
    }

    [Fact]
    public async Task ListedButUnusableNvenc_IsUnavailableNotAvailable()
    {
        Assert.SkipUnless(HardwareHosts.DistributionFFmpeg is not null, "Debian FFmpeg (/usr/bin/ffmpeg) is not installed.");
        var (ffmpeg, _) = HardwareHosts.DistributionFFmpeg!.Value;
        var capabilities = await CapabilitiesAsync(ffmpeg);
        Assert.SkipUnless(capabilities.HasEncoder("h264_nvenc"), "This FFmpeg build does not list NVENC.");
        Assert.SkipWhen(HardwareHosts.ExpectedVendor == "nvidia", "An NVIDIA GPU is expected to work here.");

        var probe = await EncoderProber.ProbeAsync(ffmpeg, new NvencStrategy(), VideoCodec.H264, capabilities, Token);

        Assert.Equal(EncoderStatus.Unavailable, probe.Status);
        Assert.Equal(FailureCategory.HardwareUnavailable, probe.Failure?.Category);
        Assert.Contains("libcuda", probe.Failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnlistedEncoder_IsNotListedWithoutRunningFFmpeg()
    {
        var capabilities = new FFmpegCapabilities { Version = new FFmpegVersion("8.0") };

        var probe = await EncoderProber.ProbeAsync("/nonexistent/ffmpeg", new AmfStrategy(), VideoCodec.Hevc, capabilities, Token);

        Assert.Equal(EncoderStatus.NotListed, probe.Status);
        Assert.Equal("hevc_amf", probe.Encoder);
    }

    [Fact]
    public async Task WindowsBuild_EveryListedGpuEncoderProbesToAClearOutcome()
    {
        Assert.SkipUnless(HardwareHosts.WindowsFFmpeg is not null, "No Windows FFmpeg reachable from this host.");
        var ffmpeg = HardwareHosts.WindowsFFmpeg!;
        var capabilities = await CapabilitiesAsync(ffmpeg);
        var registry = HardwareRegistry.For(capabilities);

        foreach (var codec in new[] { VideoCodec.H264, VideoCodec.Hevc, VideoCodec.Av1 })
        {
            var probes = await EncoderProber.ProbeAllAsync(ffmpeg, registry.Gpus, codec, capabilities, Token);

            foreach (var probe in probes)
            {
                Assert.True(
                    probe.Status != EncoderStatus.Unavailable || probe.Failure?.Category == FailureCategory.HardwareUnavailable,
                    $"{probe.Encoder}: {probe.Failure?.Category} {probe.Failure?.Message}");
                Assert.False(probe.Status == EncoderStatus.Available && probe.Failure is not null);
            }
        }
    }

    [Fact]
    public async Task WindowsBuild_ExpectedAmdGpuEncodesH264AndHevc()
    {
        Assert.SkipUnless(HardwareHosts.ExpectedVendor == "amd", $"Set {HardwareHosts.ExpectedVendorVariable}=amd on a machine with an AMD GPU.");
        Assert.SkipUnless(HardwareHosts.WindowsFFmpeg is not null, "No Windows FFmpeg reachable from this host.");
        var ffmpeg = HardwareHosts.WindowsFFmpeg!;
        var capabilities = await CapabilitiesAsync(ffmpeg);

        var h264 = await EncoderProber.ProbeAsync(ffmpeg, new AmfStrategy(), VideoCodec.H264, capabilities, Token);
        var hevc = await EncoderProber.ProbeAsync(ffmpeg, new AmfStrategy(), VideoCodec.Hevc, capabilities, Token);

        Assert.True(h264.IsAvailable, h264.Failure?.Detail);
        Assert.False(h264.SupportsHighBitDepth);
        Assert.True(hevc.IsAvailable, hevc.Failure?.Detail);
    }
}
