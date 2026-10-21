using System.Globalization;
using FluxEncoder.Core.Media;
using FluxEncoder.Infrastructure.Probing;
using FluxEncoder.Infrastructure.Tests.Support;

namespace FluxEncoder.Infrastructure.Tests.Probing;

public sealed class FFprobeJsonParserTests
{
    private static MediaInfo Mkv() => FFprobeJsonParser.Parse(Fixture.Read("ffprobe", "hdr10-multitrack.mkv.json"), "fixture.mkv");

    private static MediaInfo Mp4() => FFprobeJsonParser.Parse(Fixture.Read("ffprobe", "hlg-coverart.mp4.json"), "fixture.mp4");

    [Fact]
    public void Mkv_Format()
    {
        var media = Mkv();

        Assert.Equal("fixture.mkv", media.Source);
        Assert.Equal(["matroska", "webm"], media.FormatNames);
        Assert.Equal(TimeSpan.FromSeconds(2.023), media.Duration);
        Assert.Equal(TimeSpan.FromSeconds(-0.023), media.StartTime);
        Assert.Equal(91590, media.SizeBytes);
        Assert.Equal(362194, media.BitRate);
        Assert.Equal("Fixture Movie", media.Tags["TITLE"]);
    }

    [Fact]
    public void Mkv_StreamKindsAndIndexes()
    {
        Assert.Equal(
            [MediaStreamKind.Video, MediaStreamKind.Audio, MediaStreamKind.Audio, MediaStreamKind.Subtitle, MediaStreamKind.Attachment],
            Mkv().Streams.Select(s => s.Kind));
        Assert.Equal([0, 1, 2, 3, 4], Mkv().Streams.Select(s => s.Index));
    }

    [Fact]
    public void Mkv_Video()
    {
        var video = Mkv().VideoStreams.Single();

        Assert.Equal("hevc", video.CodecName);
        Assert.Equal(320, video.Width);
        Assert.Equal(240, video.Height);
        Assert.Equal("yuv420p10le", video.PixelFormat);
        Assert.Equal(10, video.BitDepth);
        Assert.Equal(new Rational(30000, 1001), video.AverageFrameRate);
        Assert.Equal(new Rational(1, 1), video.SampleAspectRatio);
        Assert.Equal("tv", video.Color.Range);
        Assert.Equal("bt2020nc", video.Color.Matrix);
        Assert.Equal("left", video.Color.ChromaLocation);
        Assert.Equal(TimeSpan.FromSeconds(2.002), video.Duration);
    }

    [Fact]
    public void Mkv_AudioLanguageTitleAndDisposition()
    {
        var audio = Mkv().AudioStreams.ToList();

        Assert.Equal("eng", audio[0].Language?.Value);
        Assert.Equal(StreamDisposition.Default, audio[0].Disposition);
        Assert.Equal(1, audio[0].Channels);
        Assert.Equal("mono", audio[0].ChannelLayout);
        Assert.Equal(44100, audio[0].SampleRate);
        Assert.Equal("fltp", audio[0].SampleFormat);

        Assert.Equal("fre", audio[1].Language?.Value);
        Assert.Equal("Commentary", audio[1].Title);
        Assert.Equal(StreamDisposition.Comment, audio[1].Disposition);
    }

    [Fact]
    public void Mkv_SubtitleAttachmentAndChapters()
    {
        var media = Mkv();

        var subtitle = media.SubtitleStreams.Single();
        Assert.True(subtitle.IsForced);
        Assert.False(subtitle.IsBitmap);

        var attachment = media.Attachments.Single();
        Assert.Null(attachment.CodecName);
        Assert.Equal("font.ttf", attachment.FileName);
        Assert.Equal("font/ttf", attachment.MimeType);

        Assert.Equal(["Opening", "End"], media.Chapters.Select(c => c.Title));
        Assert.Equal(TimeSpan.FromSeconds(1), media.Chapters[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), media.Chapters[1].End);
    }

    [Fact]
    public void Mp4_ColorCoverArtAndChannels()
    {
        var media = Mp4();

        Assert.Equal(["mov", "mp4", "m4a", "3gp", "3g2", "mj2"], media.FormatNames);

        var video = media.MotionVideoStreams.Single();
        Assert.Equal("arib-std-b67", video.Color.Transfer);
        Assert.Equal("bt2020", video.Color.Primaries);
        Assert.Equal(HdrFormat.Hlg, video.HdrFormats);
        Assert.Equal(8, video.BitDepth);
        Assert.Equal(new Rational(25, 1), video.AverageFrameRate);

        var cover = media.CoverArt.Single();
        Assert.Equal("png", cover.CodecName);

        var audio = media.AudioStreams.Single();
        Assert.Equal(6, audio.Channels);
        Assert.Equal("5.1", audio.ChannelLayout);
        Assert.Equal("jpn", audio.Language?.Value);
        Assert.Empty(media.Chapters);
    }

    [Fact]
    public void FrameSideData_AddsMasteringDisplayAndLightLevel()
    {
        var video = Mkv().VideoStreams.Single();

        var hdr = FFprobeJsonParser.MergeFrameSideData(video.Hdr, Fixture.Read("ffprobe", "hdr10-first-frame.json"));

        Assert.Equal(new ContentLightLevel(1000, 400), hdr.ContentLightLevel);
        Assert.NotNull(hdr.MasteringDisplay);
        Assert.Equal(new Rational(34000, 50000), hdr.MasteringDisplay.RedX);
        Assert.Equal(1000.0, hdr.MasteringDisplay.MaxLuminance.Value);
        Assert.Equal(HdrFormat.Hdr10, hdr.GetFormats(new ColorInfo { Transfer = "smpte2084" }));
    }

    [Fact]
    public void StreamSideData_RotationDolbyVisionAndHdr10Plus()
    {
        const string Json = """
            { "streams": [ {
                "index": 0, "codec_type": "video", "codec_name": "hevc", "color_transfer": "smpte2084",
                "side_data_list": [
                    { "side_data_type": "Display Matrix", "displaymatrix": "...", "rotation": -90 },
                    { "side_data_type": "DOVI configuration record", "dv_profile": 8 },
                    { "side_data_type": "HDR Dynamic Metadata SMPTE2094-40 (HDR10+)" }
                ] } ] }
            """;

        var video = FFprobeJsonParser.Parse(Json, "dv.mkv").VideoStreams.Single();

        Assert.Equal(-90, video.Rotation);
        Assert.Equal(HdrFormat.Hdr10 | HdrFormat.Hdr10Plus | HdrFormat.DolbyVision, video.HdrFormats);
    }

    [Fact]
    public void LegacyRotateTag_IsRead()
    {
        const string Json = """{ "streams": [ { "index": 0, "codec_type": "video", "tags": { "rotate": "270" } } ] }""";

        Assert.Equal(270, FFprobeJsonParser.Parse(Json, "old.mp4").VideoStreams.Single().Rotation);
    }

    [Fact]
    public void Tolerates_MissingNaAndMalformedValues()
    {
        const string Json = """
            {
              "format": { "format_name": "mpegts", "duration": "N/A", "size": "abc", "bit_rate": 1.5e3 },
              "streams": [
                { "codec_type": "video", "width": 0, "avg_frame_rate": "0/0", "r_frame_rate": "N/A",
                  "sample_aspect_ratio": "0:1", "bits_per_raw_sample": "N/A", "pix_fmt": "nv12",
                  "disposition": { "default": "1", "forced": 0 }, "tags": { "language": "und" } },
                { "codec_type": "audio", "index": 7, "channels": "2", "sample_rate": "48000", "duration": "Infinity" },
                { "codec_type": "subtitle", "index": -1 },
                { "codec_type": "something-new", "index": 9 },
                "not an object"
              ],
              "chapters": [
                { "start_time": "5.0", "end_time": "1.0" },
                { "start_time": "-1", "end_time": "1.0" },
                { "start_time": "N/A" },
                { "start_time": "0", "end_time": "3", "tags": { "title": "" } }
              ]
            }
            """;

        var media = FFprobeJsonParser.Parse(Json, "live.ts");

        Assert.Null(media.Duration);
        Assert.Null(media.SizeBytes);
        Assert.Null(media.BitRate);

        var video = media.VideoStreams.Single();
        Assert.Equal(0, video.Index);
        Assert.Null(video.Width);
        Assert.Null(video.AverageFrameRate);
        Assert.Null(video.BaseFrameRate);
        Assert.Null(video.SampleAspectRatio);
        Assert.Null(video.BitDepth);
        Assert.Null(video.Language);
        Assert.Equal(StreamDisposition.Default, video.Disposition);

        var audio = media.AudioStreams.Single();
        Assert.Equal(7, audio.Index);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(48000, audio.SampleRate);
        Assert.Null(audio.Duration);

        Assert.Equal(2, media.SubtitleStreams.Single().Index);
        Assert.Equal(MediaStreamKind.Data, media.Streams.Single(s => s.Index == 9).Kind);
        Assert.Equal(4, media.Streams.Count);

        var chapter = Assert.Single(media.Chapters);
        Assert.Null(chapter.Title);
        Assert.Equal(TimeSpan.FromSeconds(3), chapter.End);
    }

    [Theory]
    [InlineData("yuv420p10le", 10)]
    [InlineData("p010le", 10)]
    [InlineData("gbrp12be", 12)]
    [InlineData("gray10le", 10)]
    [InlineData("yuv444p16", 16)]
    [InlineData("yuv420p", null)]
    [InlineData("nv12", null)]
    [InlineData("rgb24", null)]
    public void BitDepth_FromPixelFormatWhenNotReported(string pixelFormat, int? expected)
    {
        var json = $$"""{ "streams": [ { "index": 0, "codec_type": "video", "pix_fmt": "{{pixelFormat}}" } ] }""";

        Assert.Equal(expected, FFprobeJsonParser.Parse(json, "x").VideoStreams.Single().BitDepth);
    }

    [Fact]
    public void Parse_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(TimeSpan.FromSeconds(2.023), Mkv().Duration);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Parse_RejectsNonObjectOutput(string json)
    {
        Assert.Throws<FormatException>(() => FFprobeJsonParser.Parse(json, "x"));
    }

    [Fact]
    public void Parse_EmptyObjectIsEmptyMedia()
    {
        var media = FFprobeJsonParser.Parse("{}", "x");

        Assert.Empty(media.Streams);
        Assert.Empty(media.FormatNames);
        Assert.Null(media.Duration);
    }
}
