using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Tests.Media;

public sealed class MediaInfoTests
{
    private static readonly MediaInfo Movie = new()
    {
        Source = "movie.mkv",
        FormatNames = ["matroska", "webm"],
        Duration = TimeSpan.FromMinutes(90),
        Streams =
        [
            new VideoStream { Index = 0, CodecName = "hevc", Disposition = StreamDisposition.Default },
            new AudioStream { Index = 1, CodecName = "eac3", Channels = 6 },
            new SubtitleStream { Index = 2, CodecName = "hdmv_pgs_subtitle", Disposition = StreamDisposition.Forced },
            new SubtitleStream { Index = 3, CodecName = "subrip" },
            new AttachmentStream { Index = 4, FileName = "font.ttf", MimeType = "font/ttf" },
            new VideoStream { Index = 5, CodecName = "mjpeg", Disposition = StreamDisposition.AttachedPicture },
            new DataStream { Index = 6 },
        ],
    };

    [Fact]
    public void StreamViews_FilterByKind()
    {
        Assert.Equal([0, 5], Movie.VideoStreams.Select(s => s.Index));
        Assert.Equal([1], Movie.AudioStreams.Select(s => s.Index));
        Assert.Equal([2, 3], Movie.SubtitleStreams.Select(s => s.Index));
        Assert.Equal([4], Movie.Attachments.Select(s => s.Index));
    }

    [Fact]
    public void CoverArt_IsSeparatedFromMotionVideo()
    {
        Assert.Equal([0], Movie.MotionVideoStreams.Select(s => s.Index));
        Assert.Equal([5], Movie.CoverArt.Select(s => s.Index));
    }

    [Fact]
    public void Kind_MatchesDerivedType()
    {
        Assert.Equal(
            [
                MediaStreamKind.Video, MediaStreamKind.Audio, MediaStreamKind.Subtitle, MediaStreamKind.Subtitle,
                MediaStreamKind.Attachment, MediaStreamKind.Video, MediaStreamKind.Data,
            ],
            Movie.Streams.Select(s => s.Kind));
    }

    [Fact]
    public void SubtitleFlags_ReflectCodecAndDisposition()
    {
        var subtitles = Movie.SubtitleStreams.ToList();

        Assert.True(subtitles[0].IsBitmap);
        Assert.True(subtitles[0].IsForced);
        Assert.False(subtitles[1].IsBitmap);
        Assert.False(subtitles[1].IsForced);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(0.04, true)]
    public void HasKnownDuration_RequiresPositiveDuration(double? seconds, bool expected)
    {
        var info = Movie with { Duration = seconds is { } s ? TimeSpan.FromSeconds(s) : null };

        Assert.Equal(expected, info.HasKnownDuration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Source_IsRequired(string source)
    {
        Assert.Throws<ArgumentException>(() => new MediaInfo { Source = source });
    }

    [Fact]
    public void StreamIndex_CannotBeNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioStream { Index = -1 });
    }

    [Fact]
    public void Defaults_AreEmptyNotNull()
    {
        var info = new MediaInfo { Source = "pipe:0" };
        var stream = new DataStream { Index = 0 };

        Assert.Empty(info.FormatNames);
        Assert.Empty(info.Streams);
        Assert.Empty(info.Chapters);
        Assert.Empty(info.Tags);
        Assert.Empty(stream.Tags);
        Assert.Null(stream.Language);
        Assert.Equal(StreamDisposition.None, stream.Disposition);
    }

    [Fact]
    public void Tags_AreCaseInsensitiveAndFirstKeyWins()
    {
        var tags = MediaTags.Create([new("TITLE", "first"), new("title", "second"), new("encoder", "x")]);

        Assert.Equal("first", tags["title"]);
        Assert.Equal("x", tags["ENCODER"]);
        Assert.Equal(2, tags.Count);
    }

    [Fact]
    public void Chapter_RejectsInvertedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaChapter(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaChapter(TimeSpan.FromSeconds(-1), TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(5), new MediaChapter(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)).Duration);
    }
}
