using System.Collections.Frozen;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Planning;

public sealed class HardwarePlanningTests
{
    private static readonly MediaInfo Sdr = new()
    {
        Source = "/media/clip.mkv",
        Duration = TimeSpan.FromMinutes(1),
        SizeBytes = 100_000_000,
        Streams =
        [
            new VideoStream
            {
                Index = 0, CodecName = "h264", BitDepth = 8, Disposition = StreamDisposition.Default,
                AverageFrameRate = new Rational(25, 1), BaseFrameRate = new Rational(25, 1),
                Color = new ColorInfo { Primaries = "bt709", Transfer = "bt709", Matrix = "bt709" },
            },
            new AudioStream { Index = 1, CodecName = "aac", Channels = 2, Disposition = StreamDisposition.Default },
        ],
    };

    private static PlanningContext With(IHardwareStrategy strategy, params string[] encoders) =>
        Context(capabilities: Capabilities(encoders.Length == 0 ? [.. DefaultEncoders, "h264_amf", "hevc_amf"] : encoders)) with { Hardware = strategy };

    private static PlannedJob Plan(EncodingJob job, MediaInfo media, PlanningContext context)
    {
        var result = EncodePlanner.Plan(job, new PlanningInput(InputSource.Classify(media.Source), media), context);
        Assert.True(result.IsSuccess, result.Failure?.Message);
        return result.Value;
    }

    [Fact]
    public void Amf_Snapshot()
    {
        var job = Job("/media/out/a.mp4", new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.Fast)) with { InputPath = "/media/clip.mkv" };

        var args = Plan(job, Sdr, With(new AmfStrategy())).Plan!.Invocations.Single().Arguments;

        Assert.Equal(
        [
            "-hwaccel", "auto", "-i", "/media/clip.mkv",
            "-map", "0:0", "-map", "0:1",
            "-c:v:0", "hevc_amf",
            "-quality:v:0", "speed", "-rc:v:0", "cqp", "-qp_i:v:0", "28", "-qp_p:v:0", "28",
            "-pix_fmt:v:0", "nv12",
            "-color_primaries:v:0", "bt709", "-color_trc:v:0", "bt709", "-colorspace:v:0", "bt709",
            "-disposition:v:0", "default",
            "-c:a:0", "copy", "-disposition:a:0", "default",
            "-movflags", "+faststart",
            "-n", "-f", "mp4", "/media/out/.a.tok1.partial.mp4",
        ], args);
    }

    [Fact]
    public void CopiedVideo_GetsNoHardwareDecode()
    {
        var job = Job("/media/out/a.mkv", new VideoTarget.StreamCopy()) with { InputPath = "/media/clip.mkv" };

        var args = Plan(job, Sdr, With(new AmfStrategy())).Plan!.Invocations.Single().Arguments;

        Assert.DoesNotContain("-hwaccel", args);
    }

    [Fact]
    public void HdrOnDeviceWithout10Bit_Writes8BitAndWarns()
    {
        var planned = Plan(Job("/media/out/a.mkv"), Movie(), With(new EightBitOnlyStrategy(new AmfStrategy())));

        Assert.Contains("-pix_fmt:v:0 nv12", string.Join(' ', planned.Plan!.Invocations.Single().Arguments), StringComparison.Ordinal);
        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.HdrNotPreserved);
        Assert.DoesNotContain("x265-params", string.Join(' ', planned.Plan.Invocations.Single().Arguments), StringComparison.Ordinal);
    }

    [Fact]
    public void HdrOn10BitGpu_KeepsP010AndWarnsAboutMetadata()
    {
        var planned = Plan(Job("/media/out/a.mkv"), Movie(), With(new AmfStrategy()));

        Assert.Contains("-pix_fmt:v:0 p010le", string.Join(' ', planned.Plan!.Invocations.Single().Arguments), StringComparison.Ordinal);
        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.HardwareHdrMetadata);
    }

    [Fact]
    public void Vvc_UsesLibvvencWithExperimentalWarningAndStrictWhenFlagged()
    {
        var capabilities = Capabilities([.. DefaultEncoders, "libvvenc"]) with
        {
            Encoders = Capabilities([.. DefaultEncoders, "libvvenc"]).Encoders.Values
                .Select(e => e.Name == "libvvenc" ? e with { IsExperimental = true } : e)
                .ToDictionary(e => e.Name, StringComparer.Ordinal)
                .ToFrozenDictionary(StringComparer.Ordinal),
        };
        var job = Job("/media/out/a.mp4", new VideoTarget.Encode(VideoCodec.Vvc, QualityPreset.Balanced)) with { InputPath = "/media/clip.mkv" };

        var planned = Plan(job, Sdr, Context(capabilities: capabilities));
        var args = string.Join(' ', planned.Plan!.Invocations.Single().Arguments);

        Assert.Contains("-c:v:0 libvvenc -preset:v:0 medium -qp:v:0 28 -pix_fmt:v:0 yuv420p", args, StringComparison.Ordinal);
        Assert.Contains("-strict:v:0 experimental", args, StringComparison.Ordinal);
        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.VvcExperimental);
    }

    [Fact]
    public void Vvc_WithoutLibvvencIsUnsupported()
    {
        var job = Job("/media/out/a.mkv", new VideoTarget.Encode(VideoCodec.Vvc, QualityPreset.Balanced)) with { InputPath = "/media/clip.mkv" };

        var failure = EncodePlanner.Plan(job, new PlanningInput(InputSource.Classify(Sdr.Source), Sdr), Context()).Failure;

        Assert.Equal(FailureCategory.Unsupported, failure?.Category);
        Assert.Contains("libvvenc", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Webm_RejectsVvc()
    {
        var capabilities = Capabilities([.. DefaultEncoders, "libvvenc"]);
        var job = Job("/media/out/a.webm", new VideoTarget.Encode(VideoCodec.Vvc, QualityPreset.Balanced)) with { InputPath = "/media/clip.mkv" };

        var failure = EncodePlanner.Plan(job, new PlanningInput(InputSource.Classify(Sdr.Source), Sdr), Context(capabilities: capabilities)).Failure;

        Assert.Equal(FailureCategory.Unsupported, failure?.Category);
    }
}
