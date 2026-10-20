using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;

namespace FluxEncoder.Core.Tests.Encoding;

public sealed class EncodingJobTests
{
    private static readonly EncodingJob Job = new()
    {
        InputPath = "in.mkv",
        OutputPath = "out.mp4",
        Video = new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.High),
        Audio = new AudioTarget.StreamCopy(),
    };

    [Fact]
    public void Hardware_DefaultsToAutomaticSelection()
    {
        Assert.Null(Job.Hardware);
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var same = new EncodingJob
        {
            InputPath = "in.mkv",
            OutputPath = "out.mp4",
            Video = new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.High),
            Audio = new AudioTarget.StreamCopy(),
        };

        Assert.Equal(Job, same);
        Assert.NotEqual(Job, Job with { Hardware = HardwareVendor.Nvidia });
        Assert.NotEqual(Job, Job with { Audio = new AudioTarget.Disabled() });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Paths_AreRequired(string path)
    {
        Assert.Throws<ArgumentException>(() => Job with { InputPath = path });
        Assert.Throws<ArgumentException>(() => Job with { OutputPath = path });
    }

    [Fact]
    public void Targets_CannotBeNull()
    {
        Assert.Throws<ArgumentNullException>(() => Job with { Video = null! });
        Assert.Throws<ArgumentNullException>(() => Job with { Audio = null! });
    }

    [Fact]
    public void Hardware_RejectsUndefinedVendor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Job with { Hardware = (HardwareVendor)99 });
    }

    [Fact]
    public void VideoEncode_RejectsUndefinedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoTarget.Encode((VideoCodec)99, QualityPreset.Fast));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoTarget.Encode(VideoCodec.H264, (QualityPreset)99));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-128)]
    public void AudioEncode_RejectsNonPositiveBitRate(int kbps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioTarget.Encode(AudioCodec.Aac, kbps));
    }

    [Fact]
    public void AudioEncode_BitRateIsOptional()
    {
        Assert.Null(new AudioTarget.Encode(AudioCodec.Flac).BitRateKbps);
        Assert.Equal(192, new AudioTarget.Encode(AudioCodec.Aac, 192).BitRateKbps);
    }

    [Fact]
    public void Targets_AreClosedSetsForPatternMatching()
    {
        string Describe(VideoTarget target) => target switch
        {
            VideoTarget.StreamCopy => "copy",
            VideoTarget.Disabled => "none",
            VideoTarget.Encode e => $"{e.Codec}/{e.Quality}",
            _ => throw new InvalidOperationException(),
        };

        Assert.Equal("Hevc/High", Describe(Job.Video));
        Assert.Equal("copy", Describe(new VideoTarget.StreamCopy()));
        Assert.Equal("none", Describe(new VideoTarget.Disabled()));
    }
}
