using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using FluxEncoder.Infrastructure.Execution;
using FluxEncoder.Infrastructure.Planning;
using FluxEncoder.Infrastructure.Probing;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Tests.Planning;

/// <summary>
/// End to end against real FFmpeg: probe generated media, plan, execute, and probe the result. This is what proves
/// the planner's arguments are ones FFmpeg accepts and that preservation rules survive a real encode.
/// </summary>
public sealed class PlanAndExecuteTests : IAsyncLifetime
{
    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-e2e-" + Guid.NewGuid().ToString("N"));
    private FFmpegCapabilities _capabilities = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        if (!Tools.IsComplete)
        {
            return;
        }

        _capabilities = (await FFmpegCapabilityProbe.ProbeAsync(Tools.FFmpeg!.Path)).Value!;

        await File.WriteAllTextAsync(Path.Combine(_directory, "subs.srt"), "1\n00:00:00,000 --> 00:00:01,000\nHello\n");
        await File.WriteAllTextAsync(Path.Combine(_directory, "meta.txt"), ";FFMETADATA1\ntitle=Fixture\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1500\ntitle=One\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=1500\nEND=3000\ntitle=Two\n");
        await File.WriteAllBytesAsync(Path.Combine(_directory, "font.ttf"), new byte[256]);

        await GenerateAsync("source.mkv",
        [
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
            "-f", "lavfi", "-i", "sine=frequency=880:duration=3",
            "-i", Path.Combine(_directory, "subs.srt"), "-i", Path.Combine(_directory, "meta.txt"),
            "-map", "0", "-map", "1", "-map", "2", "-map", "3", "-map_metadata", "4", "-map_chapters", "4",
            "-c:v", "libx264", "-g", "25", "-c:a", "aac", "-ac", "2", "-c:s", "srt",
            "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=fre", "-metadata:s:a:1", "title=Commentary",
            "-disposition:a:0", "default", "-disposition:a:1", "comment",
            "-metadata:s:s:0", "language=eng", "-disposition:s:0", "forced",
            "-attach", Path.Combine(_directory, "font.ttf"), "-metadata:s:t", "mimetype=font/ttf", "-metadata:s:t", "filename=font.ttf",
        ]);

        await GenerateAsync("hdr10.mkv",
        [
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=2",
            "-c:v", "libx265", "-pix_fmt", "yuv420p10le",
            "-x265-params", "log-level=error:hdr10=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400",
            "-color_primaries", "bt2020", "-color_trc", "smpte2084", "-colorspace", "bt2020nc",
        ]);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private async Task GenerateAsync(string name, string[] arguments)
    {
        var output = await ProcessCapture.RunAsync(Tools.FFmpeg!.Path, ["-hide_banner", "-loglevel", "error", "-y", .. arguments, Path.Combine(_directory, name)], TimeSpan.FromSeconds(120));
        Assert.True(output.Succeeded, output.StandardError);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string PathOf(string name) => Path.Combine(_directory, name);

    private async Task<(JobResult Result, PlannedJob Planned)> RunAsync(EncodingJob job)
    {
        Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");

        var probed = await MediaProber.ProbeAsync(Tools.FFprobe!.Path, job.InputPath, Token);
        Assert.True(probed.IsSuccess, probed.Failure?.Message);

        var context = new PlanningContext
        {
            Capabilities = _capabilities,
            Hardware = new CpuStrategy(),
            FileSystem = LocalPlanningFileSystem.Instance,
            ScratchRoot = _directory,
        };

        var planned = EncodePlanner.Plan(job, probed.Value.ToPlanningInput(), context);
        Assert.True(planned.IsSuccess, planned.Failure?.Message);
        Assert.False(planned.Value.IsSkipped);

        var result = await EncodeExecutor.ExecuteAsync(Tools.FFmpeg!.Path, planned.Value.Plan!, planned.Value.Timeline, cancellationToken: Token);
        Assert.True(result.Outcome == JobOutcome.Succeeded, $"{result.Failure?.Message}\n{result.Failure?.Detail}\n{planned.Value.Plan!.Invocations[^1].ToDisplayString()}");
        return (result, planned.Value);
    }

    private async Task<MediaInfo> ProbeOutputAsync(string path)
    {
        var probed = await MediaProber.ProbeAsync(Tools.FFprobe!.Path, path, Token);
        Assert.True(probed.IsSuccess, probed.Failure?.Message);
        return probed.Value.Media;
    }

    private static EncodingJob Job(string input, string output, VideoTarget video, AudioTarget? audio = null) => new()
    {
        InputPath = input,
        OutputPath = output,
        Video = video,
        Audio = audio ?? new AudioTarget.StreamCopy(),
    };

    [Fact]
    public async Task MkvToMp4_KeepsTracksDispositionsAndConvertsSubtitles()
    {
        var output = PathOf("out/converted.mp4");

        var (result, planned) = await RunAsync(Job(PathOf("source.mkv"), output, new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Fast)));
        var media = await ProbeOutputAsync(output);

        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.AttachmentsDropped);
        Assert.Equal("h264", media.MotionVideoStreams.Single().CodecName);

        var audio = media.AudioStreams.ToList();
        Assert.Equal(2, audio.Count);
        Assert.Equal("eng", audio[0].Language?.Value);
        Assert.True(audio[0].IsDefault);
        Assert.Equal("fre", audio[1].Language?.Value);
        Assert.True(audio[1].Disposition.HasFlag(StreamDisposition.Comment));
        Assert.False(audio[1].IsDefault);

        var subtitle = media.SubtitleStreams.Single();
        Assert.Equal("mov_text", subtitle.CodecName);
        Assert.True(subtitle.IsForced);

        Assert.Equal(2, media.Chapters.Count);
        Assert.Empty(media.Attachments);
        Assert.Empty(Directory.GetFiles(PathOf("out"), ".*partial*"));
        Assert.Equal(JobOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task MkvRemux_CopiesEverythingIncludingFont()
    {
        var output = PathOf("remux.mkv");

        await RunAsync(Job(PathOf("source.mkv"), output, new VideoTarget.StreamCopy()));
        var media = await ProbeOutputAsync(output);

        Assert.Equal(2, media.AudioStreams.Count());
        Assert.Equal("subrip", media.SubtitleStreams.Single().CodecName);
        Assert.Equal("font.ttf", media.Attachments.Single().FileName);
        Assert.Equal("Fixture", media.Tags["title"]);
    }

    [Fact]
    public async Task Hdr10HevcReencode_PreservesColourAndMasteringMetadata()
    {
        var output = PathOf("hdr-out.mkv");

        await RunAsync(Job(PathOf("hdr10.mkv"), output, new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.Fast)));
        var video = (await ProbeOutputAsync(output)).MotionVideoStreams.Single();

        Assert.Equal("yuv420p10le", video.PixelFormat);
        Assert.Equal("smpte2084", video.Color.Transfer);
        Assert.Equal("bt2020", video.Color.Primaries);
        Assert.Equal(HdrFormat.Hdr10, video.HdrFormats);
        Assert.NotNull(video.Hdr.MasteringDisplay);
        Assert.Equal(new ContentLightLevel(1000, 400), video.Hdr.ContentLightLevel);
    }

    [Fact]
    public async Task Trim_ProducesRequestedLength()
    {
        var output = PathOf("trimmed.mkv");
        var job = Job(PathOf("source.mkv"), output, new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Fast)) with
        {
            Trim = new TrimRange(start: TimeSpan.FromSeconds(1), end: TimeSpan.FromSeconds(2.5)),
        };

        await RunAsync(job);
        var media = await ProbeOutputAsync(output);

        // Copied audio must be cut too, not only the re-encoded video.
        Assert.InRange(media.Duration!.Value.TotalSeconds, 1.4, 1.65);
        Assert.All(media.Streams.Where(s => s.Kind is MediaStreamKind.Video or MediaStreamKind.Audio), s => Assert.InRange(s.Duration!.Value.TotalSeconds, 1.4, 1.65));
    }

    [Fact]
    public async Task TwoPass_EncodesAndRemovesPassLogs()
    {
        var output = PathOf("twopass.mp4");
        var video = new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Fast) { TargetBitRateKbps = 300, TwoPass = true };

        var (_, planned) = await RunAsync(Job(PathOf("source.mkv"), output, video, new AudioTarget.Encode(AudioCodec.Aac)));

        Assert.Equal(2, planned.Plan!.Invocations.Length);
        Assert.False(Directory.Exists(planned.Plan.ScratchDirectory));
        Assert.Equal("h264", (await ProbeOutputAsync(output)).MotionVideoStreams.Single().CodecName);
    }

    [Fact]
    public async Task AudioExtraction_ToMp3()
    {
        var output = PathOf("audio.mp3");
        var job = Job(PathOf("source.mkv"), output, new VideoTarget.StreamCopy(), new AudioTarget.Encode(AudioCodec.Mp3)) with
        {
            Streams = new StreamSelection { Audio = new TrackSelection.Languages([LanguageTag.TryParse("fr", out var fr) ? fr : default]) },
        };

        var (_, planned) = await RunAsync(job);
        var media = await ProbeOutputAsync(output);

        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.VideoDropped);
        Assert.Equal("mp3", media.AudioStreams.Single().CodecName);
        Assert.Empty(media.MotionVideoStreams);
    }
}
