using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Tests.Media;

public sealed class ProbeValidatorTests
{
    private static readonly InputSource Local = InputSource.Classify("in.mkv");
    private static readonly InputSource Live = InputSource.Classify("rtmp://live/app");

    private static MediaInfo Media(TimeSpan? duration, params MediaStream[] streams) =>
        new() { Source = "in.mkv", Duration = duration, Streams = streams };

    [Fact]
    public void ValidFile_HasNoFailureOrWarnings()
    {
        var media = Media(TimeSpan.FromMinutes(1), new VideoStream { Index = 0, CodecName = "h264" }, new AudioStream { Index = 1, CodecName = "aac" });

        var (failure, warnings) = ProbeValidator.Validate(media, Local);

        Assert.Null(failure);
        Assert.Empty(warnings);
    }

    [Fact]
    public void NoStreams_IsInvalidInput()
    {
        var (failure, _) = ProbeValidator.Validate(Media(TimeSpan.FromMinutes(1)), Local);

        Assert.Equal(FailureCategory.InvalidInput, failure?.Category);
    }

    [Fact]
    public void OnlySubtitlesAndCoverArt_IsInvalidInput()
    {
        var media = Media(
            TimeSpan.FromMinutes(1),
            new SubtitleStream { Index = 0, CodecName = "subrip" },
            new VideoStream { Index = 1, CodecName = "mjpeg", Disposition = StreamDisposition.AttachedPicture });

        var (failure, _) = ProbeValidator.Validate(media, Local);

        Assert.Equal(FailureCategory.InvalidInput, failure?.Category);
        Assert.Contains("subtitle", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioOnly_IsValid()
    {
        var (failure, _) = ProbeValidator.Validate(Media(TimeSpan.FromMinutes(3), new AudioStream { Index = 0, CodecName = "flac" }), Local);

        Assert.Null(failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void LocalFileWithoutDuration_Warns(double? seconds)
    {
        var media = Media(seconds is { } s ? TimeSpan.FromSeconds(s) : null, new AudioStream { Index = 0, CodecName = "aac" });

        var (failure, warnings) = ProbeValidator.Validate(media, Local);

        Assert.Null(failure);
        Assert.Equal(WarningCodes.DurationUnknown, Assert.Single(warnings).Code);
    }

    [Fact]
    public void LiveInputWithoutDuration_DoesNotWarn()
    {
        var (_, warnings) = ProbeValidator.Validate(Media(null, new VideoStream { Index = 0, CodecName = "h264" }), Live);

        Assert.Empty(warnings);
    }

    [Fact]
    public void UnknownCodec_WarnsPerStreamButNotForAttachments()
    {
        var media = Media(
            TimeSpan.FromMinutes(1),
            new VideoStream { Index = 0, CodecName = "h264" },
            new AudioStream { Index = 1 },
            new AttachmentStream { Index = 2 });

        var (_, warnings) = ProbeValidator.Validate(media, Local);

        var warning = Assert.Single(warnings);
        Assert.Equal(WarningCodes.UnknownCodec, warning.Code);
        Assert.Contains("Stream 1", warning.Message, StringComparison.Ordinal);
    }
}
