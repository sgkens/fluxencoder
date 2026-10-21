using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Tests.Encoding;

public sealed class StreamMapperTests
{
    private static readonly EncodingJob Job = new()
    {
        InputPath = "in.mkv",
        OutputPath = "out.mkv",
        Video = new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.Balanced),
        Audio = new AudioTarget.StreamCopy(),
    };

    // 0 main video (default), 1 alt angle, 2 eng audio, 3 jpn audio (default), 4 eng commentary,
    // 5 eng subs, 6 fre subs (default), 7 untagged subs, 8 cover art, 9 timecode, 10 font
    private static readonly MediaInfo Movie = new()
    {
        Source = "in.mkv",
        Duration = TimeSpan.FromMinutes(100),
        Streams =
        [
            new VideoStream { Index = 0, CodecName = "h264", Disposition = StreamDisposition.Default },
            new VideoStream { Index = 1, CodecName = "h264" },
            new AudioStream { Index = 2, CodecName = "eac3", Language = Lang("eng") },
            new AudioStream { Index = 3, CodecName = "aac", Language = Lang("jpn"), Disposition = StreamDisposition.Default },
            new AudioStream { Index = 4, CodecName = "aac", Language = Lang("eng"), Disposition = StreamDisposition.Comment },
            new SubtitleStream { Index = 5, CodecName = "subrip", Language = Lang("eng") },
            new SubtitleStream { Index = 6, CodecName = "ass", Language = Lang("fre"), Disposition = StreamDisposition.Default },
            new SubtitleStream { Index = 7, CodecName = "subrip" },
            new VideoStream { Index = 8, CodecName = "mjpeg", Disposition = StreamDisposition.AttachedPicture },
            new DataStream { Index = 9, CodecName = "tmcd" },
            new AttachmentStream { Index = 10, FileName = "font.ttf" },
        ],
    };

    private static LanguageTag Lang(string code) =>
        LanguageTag.TryParse(code, out var tag) ? tag : throw new ArgumentException(code);

    private static EncodingJob With(StreamSelection selection) => Job with { Streams = selection };

    private static int[] Indexes(StreamMap map) => [.. map.Streams.Select(s => s.Source.Index)];

    private static int[] Defaults(StreamMap map) =>
        [.. map.Streams.Where(s => s.Disposition.HasFlag(StreamDisposition.Default)).Select(s => s.Source.Index)];

    [Fact]
    public void Default_KeepsPrimaryVideoAllTracksCoverArtAndFontsButNotData()
    {
        var map = StreamMapper.Map(Job, Movie);

        Assert.Equal([0, 2, 3, 4, 5, 6, 7, 8, 10], Indexes(map));
        Assert.True(map.Chapters);
        Assert.True(map.Metadata);
        Assert.Empty(map.Warnings);
    }

    [Fact]
    public void Default_PreservesSourceDefaultsOnePerKind()
    {
        Assert.Equal([0, 3, 6], Defaults(StreamMapper.Map(Job, Movie)));
    }

    [Fact]
    public void Handling_TranscodesOnlyStreamsWithAnEncodeTarget()
    {
        var map = StreamMapper.Map(Job, Movie);

        Assert.Equal(StreamHandling.Transcode, map.Streams.Single(s => s.Source.Index == 0).Handling);
        Assert.All(map.Streams.Where(s => s.Source.Index != 0), s => Assert.Equal(StreamHandling.Copy, s.Handling));
    }

    [Fact]
    public void CoverArt_IsCopiedEvenWhenVideoIsEncoded()
    {
        var cover = StreamMapper.Map(Job, Movie).Streams.Single(s => s.Source.Index == 8);

        Assert.Equal(StreamHandling.Copy, cover.Handling);
        Assert.True(cover.Disposition.HasFlag(StreamDisposition.AttachedPicture));
    }

    [Fact]
    public void AudioLanguages_OrderByPreferenceAndFirstBecomesDefault()
    {
        var selection = new StreamSelection { Audio = new TrackSelection.Languages([Lang("en"), Lang("ja")]) };

        var map = StreamMapper.Map(With(selection), Movie);
        var audio = map.Streams.Where(s => s.Source.Kind == MediaStreamKind.Audio).ToList();

        Assert.Equal([2, 4, 3], audio.Select(s => s.Source.Index));
        Assert.Equal([0, 2, 6], Defaults(map));
        Assert.True(audio[1].Disposition.HasFlag(StreamDisposition.Comment));
    }

    [Fact]
    public void AudioLanguages_NoMatchKeepsDefaultAudioAndWarns()
    {
        var selection = new StreamSelection { Audio = new TrackSelection.Languages([Lang("ger")]) };

        var map = StreamMapper.Map(With(selection), Movie);

        Assert.Equal([3], map.Streams.Where(s => s.Source.Kind == MediaStreamKind.Audio).Select(s => s.Source.Index));
        Assert.Equal(WarningCodes.AudioLanguageNotFound, Assert.Single(map.Warnings).Code);
    }

    [Fact]
    public void SubtitleLanguages_MatchesBibliographicAndTwoLetterCodes()
    {
        var selection = new StreamSelection { Subtitles = new TrackSelection.Languages([Lang("fr")]) };

        var map = StreamMapper.Map(With(selection), Movie);

        Assert.Equal([6], map.Streams.Where(s => s.Source.Kind == MediaStreamKind.Subtitle).Select(s => s.Source.Index));
    }

    [Fact]
    public void SubtitleLanguages_NoMatchKeepsNoneAndWarns()
    {
        var selection = new StreamSelection { Subtitles = new TrackSelection.Languages([Lang("spa")]) };

        var map = StreamMapper.Map(With(selection), Movie);

        Assert.DoesNotContain(map.Streams, s => s.Source.Kind == MediaStreamKind.Subtitle);
        Assert.Equal(WarningCodes.SubtitleLanguageNotFound, Assert.Single(map.Warnings).Code);
    }

    [Fact]
    public void Subtitles_NeverGainADefault()
    {
        var selection = new StreamSelection { Subtitles = new TrackSelection.Languages([Lang("eng")]) };

        var map = StreamMapper.Map(With(selection), Movie);

        Assert.DoesNotContain(6, Defaults(map));
        Assert.DoesNotContain(5, Defaults(map));
    }

    [Fact]
    public void DefaultSelection_AudioFallsBackToFirstButSubtitlesDoNot()
    {
        var media = Movie with
        {
            Streams =
            [
                new AudioStream { Index = 0, Language = Lang("eng") },
                new AudioStream { Index = 1, Language = Lang("spa") },
                new SubtitleStream { Index = 2, Language = Lang("eng") },
            ],
        };
        var selection = new StreamSelection { Audio = new TrackSelection.Default(), Subtitles = new TrackSelection.Default() };

        var map = StreamMapper.Map(With(selection), media);

        Assert.Equal([0], Indexes(map));
        Assert.Equal([0], Defaults(map));
    }

    [Fact]
    public void AudioWithoutAnyDefault_FirstKeptBecomesDefault()
    {
        var media = Movie with
        {
            Streams = [new AudioStream { Index = 0 }, new AudioStream { Index = 1 }],
        };

        Assert.Equal([0], Defaults(StreamMapper.Map(Job, media)));
    }

    [Fact]
    public void VideoAll_KeepsEveryAngleWithOneDefault()
    {
        var map = StreamMapper.Map(With(new StreamSelection { Video = VideoSelection.All }), Movie);

        Assert.Equal([0, 1], map.Streams.Where(s => s.Source is VideoStream { IsAttachedPicture: false }).Select(s => s.Source.Index));
        Assert.Contains(0, Defaults(map));
        Assert.DoesNotContain(1, Defaults(map));
    }

    [Fact]
    public void OptionalParts_CanBeTurnedOffOrOn()
    {
        var selection = new StreamSelection
        {
            Video = VideoSelection.None,
            Subtitles = new TrackSelection.None(),
            Chapters = false,
            Metadata = false,
            CoverArt = false,
            Attachments = false,
            DataStreams = true,
        };

        var map = StreamMapper.Map(With(selection), Movie);

        Assert.Equal([2, 3, 4, 9], Indexes(map));
        Assert.False(map.Chapters);
        Assert.False(map.Metadata);
    }

    [Fact]
    public void NothingSelected_IsEmpty()
    {
        var selection = new StreamSelection
        {
            Video = VideoSelection.None,
            Audio = new TrackSelection.None(),
            Subtitles = new TrackSelection.None(),
            CoverArt = false,
            Attachments = false,
        };

        Assert.True(StreamMapper.Map(With(selection), Movie).IsEmpty);
    }

    [Fact]
    public void AudioOnlyInput_HasNoVideoAndNoWarnings()
    {
        var media = Movie with { Streams = [new AudioStream { Index = 0, CodecName = "flac" }] };

        var map = StreamMapper.Map(Job, media);

        Assert.Equal([0], Indexes(map));
        Assert.Empty(map.Warnings);
    }

    [Fact]
    public void Languages_RejectsEmptyOrBlankTags()
    {
        Assert.Throws<ArgumentException>(() => new TrackSelection.Languages([]));
        Assert.Throws<ArgumentException>(() => new TrackSelection.Languages([default]));
    }

    [Fact]
    public void Languages_EqualityComparesPreferenceSequence()
    {
        Assert.Equal(new TrackSelection.Languages([Lang("eng"), Lang("jpn")]), new TrackSelection.Languages([Lang("eng"), Lang("jpn")]));
        Assert.NotEqual(new TrackSelection.Languages([Lang("eng"), Lang("jpn")]), new TrackSelection.Languages([Lang("jpn"), Lang("eng")]));
    }
}
