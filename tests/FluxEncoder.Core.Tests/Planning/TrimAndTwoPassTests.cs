using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Planning;

public sealed class TrimAndTwoPassTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static Result<PlannedJob> Plan(EncodingJob job, PlanningInput? input = null) =>
        EncodePlanner.Plan(job, input ?? Input(), Context());

    private static EncodingJob Trimmed(TrimRange trim, VideoTarget? video = null) =>
        Job("/media/out/clip.mkv", video) with { Trim = trim };

    private static IReadOnlyList<string> Head(PlannedJob planned, int count) =>
        [.. planned.Plan!.Invocations[^1].Arguments.Take(count)];

    [Fact]
    public void StartAndEnd_AreAbsoluteAndBecomeSeekPlusLength()
    {
        var planned = Plan(Trimmed(new TrimRange(start: Minute, end: 3 * Minute))).Value!;

        Assert.Equal(["-ss", "60", "-protocol_whitelist", "file,crypto,data", "-i", "/media/movie.mkv", "-ss", "0", "-t", "120"], Head(planned, 10));
        Assert.Equal(new InputTimeline.Finite(2 * Minute), planned.Timeline);
    }

    [Fact]
    public void StartAndDuration_AreSeekPlusLength()
    {
        var planned = Plan(Trimmed(new TrimRange(start: TimeSpan.FromSeconds(90.5), duration: TimeSpan.FromSeconds(30)))).Value!;

        Assert.Equal(["-ss", "90.5", "-protocol_whitelist", "file,crypto,data", "-i", "/media/movie.mkv", "-ss", "0", "-t", "30"], Head(planned, 10));
    }

    [Fact]
    public void StartOnly_EncodesToTheEnd()
    {
        var planned = Plan(Trimmed(new TrimRange(start: 90 * Minute))).Value!;

        Assert.Equal(["-ss", "5400", "-protocol_whitelist", "file,crypto,data", "-i", "/media/movie.mkv", "-ss", "0", "-map"], Head(planned, 9));
        Assert.Equal(new InputTimeline.Finite(10 * Minute), planned.Timeline);
    }

    [Fact]
    public void EndOnly_IsLengthFromZero()
    {
        var planned = Plan(Trimmed(new TrimRange(end: 5 * Minute))).Value!;

        Assert.Equal(["-protocol_whitelist", "file,crypto,data", "-i", "/media/movie.mkv", "-t", "300"], Head(planned, 6));
    }

    [Fact]
    public void StartPastEnd_IsInvalidUsage()
    {
        var failure = Plan(Trimmed(new TrimRange(start: 100 * Minute))).Failure;

        Assert.Equal(FailureCategory.InvalidUsage, failure?.Category);
    }

    [Fact]
    public void EndPastInput_IsClampedWithWarning()
    {
        var planned = Plan(Trimmed(new TrimRange(start: 95 * Minute, end: 120 * Minute))).Value!;

        Assert.Equal(["-ss", "5700", "-protocol_whitelist", "file,crypto,data", "-i", "/media/movie.mkv", "-ss", "0", "-t", "300"], Head(planned, 10));
        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.TrimEndBeyondInput);
    }

    [Fact]
    public void CopiedVideoWithStart_WarnsAboutKeyframes()
    {
        var planned = Plan(Trimmed(new TrimRange(start: Minute), new VideoTarget.StreamCopy())).Value!;

        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.TrimKeyframeAligned);
        Assert.Equal(["-ss", "60", "-protocol_whitelist", "file,crypto,data", "-i", "/media/movie.mkv", "-map"], Head(planned, 7));
    }

    [Fact]
    public void LiveInput_AcceptsOnlyDurationAsRecordingLimit()
    {
        var live = Movie() with { Source = "rtmp://live/app", Duration = null };
        var input = new PlanningInput(InputSource.Classify(live.Source), live);

        var limited = Plan(Trimmed(new TrimRange(duration: 10 * Minute)), input).Value!;
        Assert.Equal(["-protocol_whitelist", "rtmp,rtmps,rtmpt,rtmpts,rtmpe,rtmpte,tcp,tls,http,https", "-i", "rtmp://live/app", "-t", "600"], Head(limited, 6));
        Assert.IsType<InputTimeline.Unbounded>(limited.Timeline);

        Assert.Equal(FailureCategory.InvalidUsage, Plan(Trimmed(new TrimRange(start: Minute)), input).Failure?.Category);
        Assert.Equal(FailureCategory.InvalidUsage, Plan(Trimmed(new TrimRange(end: Minute)), input).Failure?.Category);
    }

    [Fact]
    public void TrimRange_RejectsContradictions()
    {
        Assert.Throws<ArgumentException>(() => new TrimRange());
        Assert.Throws<ArgumentException>(() => new TrimRange(end: Minute, duration: Minute));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrimRange(start: 2 * Minute, end: Minute));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrimRange(start: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrimRange(duration: TimeSpan.Zero));
    }

    [Fact]
    public void TwoPass_BuildsNullFirstPassAndSharedLog()
    {
        var video = new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Balanced) { TargetBitRateKbps = 4000, TwoPass = true };
        var job = Job("/media/out/movie.mkv", video) with { Streams = new StreamSelection { Subtitles = new TrackSelection.None(), Attachments = false } };

        var planned = Plan(job).Value!;
        var plan = planned.Plan!;

        Assert.Equal("/tmp/flux-tok1", plan.ScratchDirectory);
        Assert.Equal(2, plan.Invocations.Length);

        Assert.Equal(
        [
            "-protocol_whitelist", "file,crypto,data", "-i", "/media/movie.mkv",
            "-map", "0:0",
            "-c:v:0", "libx264", "-preset:v:0", "medium", "-b:v:0", "4000k", "-pix_fmt:v:0", "yuv420p",
            "-color_range:v:0", "tv", "-color_primaries:v:0", "bt2020", "-color_trc:v:0", "smpte2084", "-colorspace:v:0", "bt2020nc",
            "-metadata:s:v:0", "BPS-eng=", "-metadata:s:v:0", "DURATION-eng=",
            "-pass", "1", "-passlogfile", "/tmp/flux-tok1/pass",
            "-an", "-sn", "-dn", "-f", "null", "/dev/null",
        ], plan.Invocations[0].Arguments);

        var second = string.Join(' ', plan.Invocations[1].Arguments);
        Assert.Contains("-b:v:0 4000k", second, StringComparison.Ordinal);
        Assert.DoesNotContain("-crf", second, StringComparison.Ordinal);
        Assert.Contains("-pass 2 -passlogfile /tmp/flux-tok1/pass -n -f matroska", second, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPass_RequiresBitRate()
    {
        var video = new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Balanced) { TwoPass = true };

        var failure = Plan(Job("/media/out/a.mkv", video)).Failure;

        Assert.Equal(FailureCategory.InvalidUsage, failure?.Category);
        Assert.Contains("bit rate", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPass_NotOfferedForX265()
    {
        var video = new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.Balanced) { TargetBitRateKbps = 4000, TwoPass = true };

        Assert.Equal(FailureCategory.Unsupported, Plan(Job("/media/out/a.mkv", video)).Failure?.Category);
    }

    [Fact]
    public void TwoPass_RejectsLiveInput()
    {
        var live = Movie() with { Source = "rtmp://live/app", Duration = null };
        var video = new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Balanced) { TargetBitRateKbps = 4000, TwoPass = true };

        var failure = Plan(Job("/media/out/a.mkv", video), new PlanningInput(InputSource.Classify(live.Source), live)).Failure;

        Assert.Equal(FailureCategory.InvalidUsage, failure?.Category);
    }

    [Fact]
    public void TargetBitRate_WithoutTwoPass_ReplacesCrf()
    {
        var video = new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.Fast) { TargetBitRateKbps = 2500 };

        var args = string.Join(' ', Plan(Job("/media/out/a.mkv", video)).Value!.Plan!.Invocations.Single().Arguments);

        Assert.Contains("-c:v:0 libx265 -preset:v:0 veryfast -b:v:0 2500k", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-crf", args, StringComparison.Ordinal);
    }
}
