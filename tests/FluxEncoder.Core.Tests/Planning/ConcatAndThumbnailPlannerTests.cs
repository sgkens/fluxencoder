using System.Collections.Frozen;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Planning;

public sealed class ConcatAndThumbnailPlannerTests
{
    private static PlanningInput Clip(string path, int width = 1280, int height = 720, string video = "h264", string? audio = "aac", double seconds = 60, Rational? rate = null)
    {
        List<MediaStream> streams =
        [
            new VideoStream
            {
                Index = 0, CodecName = video, Width = width, Height = height, PixelFormat = "yuv420p", BitDepth = 8,
                AverageFrameRate = rate ?? new Rational(25, 1), SampleAspectRatio = new Rational(1, 1),
            },
        ];

        if (audio is not null)
        {
            streams.Add(new AudioStream { Index = 1, CodecName = audio, SampleRate = 48000, Channels = 2 });
        }

        var media = new MediaInfo { Source = path, Duration = TimeSpan.FromSeconds(seconds), SizeBytes = 10_000_000, Streams = streams };
        return new PlanningInput(InputSource.Classify(path), media);
    }

    private static ConcatJob Concat(string output = "/media/out/joined.mkv", VideoTarget? video = null, ConcatMode mode = ConcatMode.Auto) =>
        new(["/media/a.mkv", "/media/b.mkv"], output, video ?? new VideoTarget.StreamCopy(), new AudioTarget.StreamCopy()) { Mode = mode };

    [Fact]
    public void CompatibleInputs_CopyThroughConcatList()
    {
        var planned = ConcatPlanner.Plan(Concat(), [Clip("/media/a.mkv"), Clip("/media/b.mkv", seconds: 30)], Context()).Value!;
        var plan = planned.Plan!;

        Assert.Equal(
            ["-f", "concat", "-safe", "0", "-i", "/tmp/flux-tok1/inputs.ffconcat", "-map", "0:v:0", "-map", "0:a:0?", "-c", "copy", "-n", "-f", "matroska", "/media/out/.joined.tok1.partial.mkv"],
            plan.Invocations.Single().Arguments);
        Assert.Equal("ffconcat version 1.0\nfile '/media/a.mkv'\nfile '/media/b.mkv'\n", plan.PreparedFiles["/tmp/flux-tok1/inputs.ffconcat"]);
        Assert.Equal(new InputTimeline.Finite(TimeSpan.FromSeconds(90)), planned.Timeline);
        Assert.Empty(planned.Warnings);
    }

    [Fact]
    public void ConcatList_EscapesQuotes()
    {
        Assert.Equal("ffconcat version 1.0\nfile '/m/it'\\''s.mkv'\n", ConcatPlanner.ConcatList(["/m/it's.mkv"]));
    }

    [Fact]
    public void CompatibilityProblems_NameEachDifference()
    {
        var problems = ConcatPlanner.CompatibilityProblems(
            [Clip("/media/a.mkv"), Clip("/media/b.mkv", width: 1920, height: 1080, audio: null, rate: new Rational(30000, 1001))]);

        Assert.Contains("b.mkv has width 1920 instead of 1280", problems);
        Assert.Contains("b.mkv has height 1080 instead of 720", problems);
        Assert.Contains("b.mkv has frame rate 30000/1001 instead of 25/1", problems);
        Assert.Contains("b.mkv has no audio", problems);
    }

    [Fact]
    public void IncompatibleInputs_ReencodeWithNormalizingFilterAndSilence()
    {
        var planned = ConcatPlanner.Plan(Concat(), [Clip("/media/a.mkv"), Clip("/media/b.mkv", width: 1920, height: 1080, audio: null, seconds: 12.5)], Context()).Value!;
        var args = planned.Plan!.Invocations.Single().Arguments;

        Assert.Equal(WarningCodes.ConcatReencoded, Assert.Single(planned.Warnings).Code);
        Assert.Equal(["-i", "/media/a.mkv", "-i", "/media/b.mkv", "-f", "lavfi", "-t", "12.5", "-i", "anullsrc=r=48000:cl=stereo"], args.Take(10));

        var filter = args[args.IndexOf("-filter_complex") + 1];
        Assert.Equal(
            "[0:v:0]scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=25/1,format=yuv420p[v0];" +
            "[0:a:0]aresample=48000,aformat=channel_layouts=stereo[a0];" +
            "[1:v:0]scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=25/1,format=yuv420p[v1];" +
            "[2:a:0]aresample=48000,aformat=channel_layouts=stereo[a1];" +
            "[v0][a0][v1][a1]concat=n=2:v=1:a=1[v][a]",
            filter);

        var joined = string.Join(' ', args);
        Assert.Contains("-map [v] -map [a] -c:v libx264 -preset:v medium -crf:v 20 -c:a libopus -b:a 96k", joined, StringComparison.Ordinal);
        Assert.Empty(planned.Plan.PreparedFiles);
    }

    [Fact]
    public void CopyMode_RejectsIncompatibleInputs()
    {
        var failure = ConcatPlanner.Plan(Concat(mode: ConcatMode.Copy), [Clip("/media/a.mkv"), Clip("/media/b.mkv", video: "hevc")], Context()).Failure;

        Assert.Equal(FailureCategory.Unsupported, failure?.Category);
        Assert.Contains("video codec hevc instead of h264", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitEncodeTarget_AlwaysReencodesWithoutWarning()
    {
        var video = new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.Fast);

        var planned = ConcatPlanner.Plan(Concat(video: video), [Clip("/media/a.mkv"), Clip("/media/b.mkv")], Context()).Value!;

        Assert.Contains("libx265", planned.Plan!.Invocations.Single().Arguments);
        Assert.Empty(planned.Warnings);
    }

    [Fact]
    public void Concat_RejectsOutputThatIsAnInputAndUnknownLengths()
    {
        var self = ConcatPlanner.Plan(Concat("/media/b.mkv"), [Clip("/media/a.mkv"), Clip("/media/b.mkv")], Context()).Failure;
        Assert.Equal(FailureCategory.OutputIsInput, self?.Category);

        var live = new PlanningInput(InputSource.Classify("rtmp://x/y"), new MediaInfo { Source = "rtmp://x/y", Streams = [new VideoStream { Index = 0 }] });
        var unbounded = ConcatPlanner.Plan(Concat(), [Clip("/media/a.mkv"), live], Context()).Failure;
        Assert.Equal(FailureCategory.InvalidInput, unbounded?.Category);
    }

    [Fact]
    public void ConcatJob_RequiresTwoInputs()
    {
        Assert.Throws<ArgumentException>(() => new ConcatJob(["/a.mkv"], "/o.mkv", new VideoTarget.StreamCopy(), new AudioTarget.StreamCopy()));
    }

    private static ThumbnailJob Sheet(string output = "/media/out/sheet.jpg", int columns = 4, int rows = 3, int tileWidth = 320) =>
        new() { InputPath = "/media/movie.mkv", OutputPath = output, Columns = columns, Rows = rows, TileWidth = tileWidth };

    private static PlanningContext ThumbnailContext(params string[] filters) =>
        Context(capabilities: Capabilities("mjpeg", "png", "libwebp") with
        {
            Muxers = new FormatCatalog([new FormatEntry(["image2"], "image2")]),
            Filters = filters.ToFrozenSet(StringComparer.Ordinal),
        });

    [Fact]
    public void Thumbnails_SnapshotForSdrClip()
    {
        var planned = ThumbnailPlanner.Plan(Sheet(), Clip("/media/a.mkv", seconds: 130), ThumbnailContext()).Value!;

        Assert.Equal(
        [
            "-ss", "10", "-i", "/media/a.mkv", "-map", "0:0",
            "-vf", "fps=1/10,scale=320:180,setsar=1,tile=4x3,format=yuvj420p",
            "-frames:v", "1", "-c:v", "mjpeg", "-q:v", "3", "-update", "1",
            "-n", "-f", "image2", "/media/out/.sheet.tok1.partial.jpg",
        ], planned.Plan!.Invocations.Single().Arguments);
        Assert.Empty(planned.Warnings);
    }

    [Fact]
    public void Thumbnails_ToneMapHdrWhenFiltersExist()
    {
        var withFilters = ThumbnailPlanner.Plan(Sheet("/media/out/sheet.png"), Input(), ThumbnailContext("zscale", "tonemap")).Value!;
        Assert.Contains("tonemap=hable", withFilters.Plan!.Invocations.Single().Arguments[7], StringComparison.Ordinal);
        Assert.Equal(WarningCodes.ThumbnailsToneMapped, Assert.Single(withFilters.Warnings).Code);

        var without = ThumbnailPlanner.Plan(Sheet("/media/out/sheet.png"), Input(), ThumbnailContext()).Value!;
        Assert.Equal(WarningCodes.ThumbnailsHdrWashedOut, Assert.Single(without.Warnings).Code);
    }

    [Fact]
    public void Thumbnails_ValidateFormatLengthFramesAndSize()
    {
        Assert.Equal(FailureCategory.InvalidUsage, ThumbnailPlanner.Plan(Sheet("/media/out/s.gif"), Clip("/media/a.mkv"), ThumbnailContext()).Failure?.Category);

        var tooShort = Clip("/media/a.mkv", seconds: 0.2);
        Assert.Equal(FailureCategory.InvalidInput, ThumbnailPlanner.Plan(Sheet(), tooShort, ThumbnailContext()).Failure?.Category);

        var huge = ThumbnailPlanner.Plan(Sheet("/media/out/s.webp", columns: 20, tileWidth: 1920), Clip("/media/a.mkv"), ThumbnailContext()).Failure;
        Assert.Equal(FailureCategory.InvalidUsage, huge?.Category);
        Assert.Contains("16383", huge!.Message, StringComparison.Ordinal);

        var noVideo = new PlanningInput(InputSource.Classify("/media/a.mp3"), new MediaInfo { Source = "/media/a.mp3", Duration = TimeSpan.FromMinutes(3), Streams = [new AudioStream { Index = 0 }] });
        Assert.Equal(FailureCategory.InvalidInput, ThumbnailPlanner.Plan(Sheet(), noVideo, ThumbnailContext()).Failure?.Category);
    }

    [Theory]
    [InlineData(1920, 1080, 1, 1, null, 320, 180)]
    [InlineData(720, 480, 32, 27, null, 320, 180)]
    [InlineData(1920, 1080, 1, 1, -90, 320, 568)]
    [InlineData(1001, 1001, 1, 1, null, 320, 320)]
    public void TileSize_UsesDisplayAspectAndRotation(int width, int height, int sarNum, int sarDen, int? rotation, int expectedWidth, int expectedHeight)
    {
        var video = new VideoStream { Index = 0, Width = width, Height = height, SampleAspectRatio = new Rational(sarNum, sarDen), Rotation = rotation };

        Assert.Equal((expectedWidth, expectedHeight), ThumbnailPlanner.TileSize(video, 320));
    }

    [Fact]
    public void ThumbnailJob_ValidatesGrid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sheet(columns: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sheet(rows: 21));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sheet(tileWidth: 10));
    }
}
