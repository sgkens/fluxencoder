using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Tests.Encoding;

public sealed class InputTimelineTests
{
    [Fact]
    public void From_KnownDuration_IsFinite()
    {
        var media = new MediaInfo { Source = "in.mkv", Duration = TimeSpan.FromMinutes(2) };

        var timeline = Assert.IsType<InputTimeline.Finite>(InputTimeline.From(media));
        Assert.Equal(TimeSpan.FromMinutes(2), timeline.Duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void From_MissingOrZeroDuration_IsUnbounded(double? seconds)
    {
        var media = new MediaInfo
        {
            Source = "rtmp://example/live",
            Duration = seconds is { } s ? TimeSpan.FromSeconds(s) : null,
        };

        Assert.IsType<InputTimeline.Unbounded>(InputTimeline.From(media));
    }

    [Fact]
    public void Finite_ReportsClampedFraction()
    {
        var timeline = new InputTimeline.Finite(TimeSpan.FromSeconds(100));

        Assert.Equal(0.25, timeline.Fraction(TimeSpan.FromSeconds(25)));
        Assert.Equal(1.0, timeline.Fraction(TimeSpan.FromSeconds(101)));
        Assert.Equal(0.0, timeline.Fraction(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Finite_StopIsCancellation()
    {
        InputTimeline timeline = new InputTimeline.Finite(TimeSpan.FromSeconds(1));

        Assert.True(timeline.SupportsPercentage);
        Assert.False(timeline.StopCompletesJob);
        Assert.True(timeline.SupportsMultiPass);
        Assert.True(timeline.SupportsJobRetry);
    }

    [Fact]
    public void Unbounded_StopCompletesAndDisablesFiniteFeatures()
    {
        InputTimeline timeline = new InputTimeline.Unbounded();

        Assert.False(timeline.SupportsPercentage);
        Assert.Null(timeline.Fraction(TimeSpan.FromHours(1)));
        Assert.True(timeline.StopCompletesJob);
        Assert.False(timeline.SupportsMultiPass);
        Assert.False(timeline.SupportsJobRetry);
    }

    [Fact]
    public void Finite_RejectsNonPositiveDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputTimeline.Finite(TimeSpan.Zero));
    }

    [Fact]
    public void StoppedByUser_IsAValidWarningCode()
    {
        Assert.Equal(WarningCodes.StoppedByUser, new JobWarning(WarningCodes.StoppedByUser, "Stopped.").Code);
    }
}
