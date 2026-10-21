using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Planning;

public sealed class EncodePlannerTests
{
    private static PlannedJob Planned(EncodingJob job, PlanningInput? input = null, PlanningContext? context = null)
    {
        var result = EncodePlanner.Plan(job, input ?? Input(), context ?? Context());
        Assert.True(result.IsSuccess, result.Failure?.Message);
        return result.Value;
    }

    private static JobFailure Failure(EncodingJob job, PlanningInput? input = null, PlanningContext? context = null)
    {
        var result = EncodePlanner.Plan(job, input ?? Input(), context ?? Context());
        Assert.False(result.IsSuccess, "Expected planning to fail.");
        return result.Failure;
    }

    private static string[] Codes(PlannedJob planned) => [.. planned.Warnings.Select(w => w.Code)];

    [Fact]
    public void MkvHevcEncode_FullSnapshot()
    {
        var planned = Planned(Job("/media/out/movie.mkv"));

        Assert.False(planned.IsSkipped);
        var plan = planned.Plan!;
        Assert.Equal("/media/out/movie.mkv", plan.FinalOutputPath);
        Assert.Equal("/media/out/.movie.tok1.partial.mkv", plan.TemporaryOutputPath);
        Assert.Null(plan.ScratchDirectory);

        string[] expected =
        [
            "-i", "/media/movie.mkv",
            "-map", "0:0", "-map", "0:1", "-map", "0:2", "-map", "0:3", "-map", "0:4", "-map", "0:5",
            "-c:v:0", "libx265",
            "-preset:v:0", "medium", "-crf:v:0", "23",
            "-pix_fmt:v:0", "yuv420p10le",
            "-color_range:v:0", "tv", "-color_primaries:v:0", "bt2020", "-color_trc:v:0", "smpte2084", "-colorspace:v:0", "bt2020nc",
            "-x265-params:v:0", "hdr10=1:repeat-headers=1:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400",
            "-metadata:s:v:0", "BPS-eng=", "-metadata:s:v:0", "DURATION-eng=",
            "-disposition:v:0", "default",
            "-c:a:0", "copy", "-disposition:a:0", "default",
            "-c:a:1", "copy", "-disposition:a:1", "comment",
            "-c:s:0", "copy", "-disposition:s:0", "0",
            "-c:s:1", "copy", "-disposition:s:1", "forced",
            "-c:t:0", "copy",
            "-n", "-f", "matroska", "/media/out/.movie.tok1.partial.mkv",
        ];

        Assert.Equal(expected, Assert.Single(plan.Invocations).Arguments);
        Assert.Equal([WarningCodes.CoverArtDropped], Codes(planned));
        Assert.Equal(new InputTimeline.Finite(TimeSpan.FromMinutes(100)), planned.Timeline);
    }

    [Fact]
    public void Mp4_ConvertsDropsAndTranscodesWithWarnings()
    {
        var planned = Planned(Job("/media/out/movie.mp4"));
        var args = string.Join(' ', planned.Plan!.Invocations[0].Arguments);

        // TrueHD is not accepted by MP4: re-encoded to AAC at 64 kbit/s per channel. AC-3 is accepted: copied.
        Assert.Contains("-c:a:0 aac -b:a:0 512k", args, StringComparison.Ordinal);
        Assert.Contains("-c:a:1 copy", args, StringComparison.Ordinal);

        // PGS dropped, ASS converted to mov_text, font dropped, cover art kept as attached picture.
        Assert.DoesNotContain("-map 0:3", args, StringComparison.Ordinal);
        Assert.Contains("-map 0:4", args, StringComparison.Ordinal);
        Assert.Contains("-c:s:0 mov_text -disposition:s:0 forced", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-map 0:5", args, StringComparison.Ordinal);
        Assert.Contains("-c:v:1 copy -disposition:v:1 attached_pic", args, StringComparison.Ordinal);
        Assert.Contains("-movflags +faststart -n -f mp4", args, StringComparison.Ordinal);

        Assert.Equal(
            [WarningCodes.AudioTranscoded, WarningCodes.AttachmentsDropped, WarningCodes.SubtitleDropped, WarningCodes.SubtitleStylingLost],
            Codes(planned));
    }

    [Fact]
    public void Webm_RejectsHevcEncodeAndH264Copy()
    {
        Assert.Equal(FailureCategory.Unsupported, Failure(Job("/media/out/a.webm")).Category);

        var h264 = Movie() with { Streams = [new VideoStream { Index = 0, CodecName = "h264" }] };
        var copy = Failure(Job("/media/out/a.webm", new VideoTarget.StreamCopy()), Input(h264));
        Assert.Equal(FailureCategory.Unsupported, copy.Category);
        Assert.Contains("h264", copy.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/media/out/movie", "no file extension")]
    [InlineData("/media/out/movie.xyz", "'.xyz' is not supported")]
    public void UnknownExtension_IsInvalidUsage(string output, string message)
    {
        var failure = Failure(Job(output));

        Assert.Equal(FailureCategory.InvalidUsage, failure.Category);
        Assert.Contains(message, failure.Message, StringComparison.Ordinal);
        Assert.Contains(".mkv", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingEncoder_IsUnsupported()
    {
        var failure = Failure(Job("/media/out/a.mkv"), context: Context(capabilities: Capabilities("libx264", "aac")));

        Assert.Equal(FailureCategory.Unsupported, failure.Category);
        Assert.Contains("libx265", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void H264FromHdr_WarnsAndStays8Bit()
    {
        var planned = Planned(Job("/media/out/a.mkv", new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Fast)));
        var args = string.Join(' ', planned.Plan!.Invocations[0].Arguments);

        Assert.Contains("-c:v:0 libx264 -preset:v:0 veryfast -crf:v:0 23 -pix_fmt:v:0 yuv420p", args, StringComparison.Ordinal);
        Assert.DoesNotContain("x265-params", args, StringComparison.Ordinal);
        Assert.Contains(WarningCodes.HdrInH264, Codes(planned));
    }

    [Fact]
    public void DolbyVisionHdr10PlusAndVfr_AreHandled()
    {
        var movie = Movie();
        var video = (VideoStream)movie.Streams[0];
        var dv = video with
        {
            Hdr = video.Hdr with { HasDolbyVisionConfiguration = true, HasHdr10PlusMetadata = true },
            BaseFrameRate = new Rational(120, 1),
        };
        var media = movie with { Streams = [dv, .. movie.Streams.Skip(1)] };

        var planned = Planned(Job("/media/out/a.mkv"), Input(media));

        Assert.Contains(WarningCodes.DolbyVisionLost, Codes(planned));
        Assert.Contains(WarningCodes.Hdr10PlusLost, Codes(planned));
        Assert.Contains("-fps_mode:v:0 passthrough", string.Join(' ', planned.Plan!.Invocations[0].Arguments), StringComparison.Ordinal);
    }

    [Fact]
    public void StreamCopy_KeepsVideoUntouched()
    {
        var planned = Planned(Job("/media/out/remux.mkv", new VideoTarget.StreamCopy()));
        var args = string.Join(' ', planned.Plan!.Invocations[0].Arguments);

        Assert.Contains("-c:v:0 copy -disposition:v:0 default", args, StringComparison.Ordinal);
        Assert.DoesNotContain("pix_fmt", args, StringComparison.Ordinal);
        Assert.DoesNotContain("BPS", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Mp3FromSurround_DownmixesAndDropsVideo()
    {
        var job = Job("/media/out/audio.mp3", audio: new AudioTarget.Encode(AudioCodec.Mp3)) with
        {
            Streams = new StreamSelection { Audio = new TrackSelection.Default() },
        };

        var planned = Planned(job);
        var args = string.Join(' ', planned.Plan!.Invocations[0].Arguments);

        Assert.Contains("-c:a:0 libmp3lame -ac:a:0 2 -b:a:0 192k", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-c:v:0 libx265", args, StringComparison.Ordinal);
        Assert.Contains(WarningCodes.VideoDropped, Codes(planned));
        Assert.Contains(WarningCodes.AudioDownmixed, Codes(planned));
    }

    [Fact]
    public void Ts_DropsChaptersWithWarning()
    {
        var planned = Planned(Job("/media/out/a.ts", audio: new AudioTarget.Encode(AudioCodec.Aac)));
        var args = string.Join(' ', planned.Plan!.Invocations[0].Arguments);

        Assert.Contains("-map_chapters -1", args, StringComparison.Ordinal);
        Assert.Contains(WarningCodes.ChaptersDropped, Codes(planned));
    }

    [Fact]
    public void MetadataAndChaptersOff_EmitClearingOptions()
    {
        var job = Job("/media/out/a.mkv") with { Streams = new StreamSelection { Metadata = false, Chapters = false } };

        var args = string.Join(' ', Planned(job).Plan!.Invocations[0].Arguments);

        Assert.Contains("-map_metadata:g -1 -map_chapters -1", args, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpInput_GetsReconnectOptions()
    {
        var media = Movie() with { Source = "https://cdn.example/v.mkv" };
        var input = new PlanningInput(InputSource.Classify(media.Source), media);

        var args = Planned(Job("/media/out/a.mkv"), input).Plan!.Invocations[0].Arguments;

        Assert.Equal(["-reconnect", "1", "-reconnect_streamed", "1", "-reconnect_delay_max", "30", "-i", "https://cdn.example/v.mkv"], args.Take(8));
    }

    [Fact]
    public void PipeInput_UsesDefaultSelectionAndRejectsCustomSelection()
    {
        var input = new PlanningInput(InputSource.Classify("pipe:0"), new MediaInfo { Source = "pipe:0" }, IsProbed: false);

        var planned = Planned(Job("/media/out/a.mkv"), input);
        Assert.Equal(
            ["-i", "pipe:0", "-c:v", "libx265", "-preset:v", "medium", "-crf:v", "23", "-c:a", "copy", "-n", "-f", "matroska", "/media/out/.a.tok1.partial.mkv"],
            planned.Plan!.Invocations[0].Arguments);
        Assert.IsType<InputTimeline.Unbounded>(planned.Timeline);

        var custom = Job("/media/out/a.mkv") with { Streams = new StreamSelection { Video = VideoSelection.All } };
        Assert.Equal(FailureCategory.InvalidUsage, Failure(custom, input).Category);
    }

    [Fact]
    public void Planning_IsDeterministic()
    {
        Assert.Equal(Planned(Job("/media/out/a.mp4")), Planned(Job("/media/out/a.mp4")));
    }

    [Fact]
    public void Dispositions_RenderFfmpegNames()
    {
        Assert.Equal("0", EncodePlanner.DispositionText(StreamDisposition.None));
        Assert.Equal("default+forced+hearing_impaired", EncodePlanner.DispositionText(StreamDisposition.Default | StreamDisposition.Forced | StreamDisposition.HearingImpaired));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1.5, "1.5")]
    [InlineData(3600.0000001, "3600")]
    [InlineData(59.123456, "59.123456")]
    public void FormatSeconds_IsInvariant(double seconds, string expected)
    {
        Assert.Equal(expected, EncodePlanner.FormatSeconds(TimeSpan.FromSeconds(seconds)));
    }
}
