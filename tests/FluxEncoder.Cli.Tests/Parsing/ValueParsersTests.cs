using FluxEncoder.Cli.Parsing;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Cli.Tests.Parsing;

public sealed class ValueParsersTests
{
    [Theory]
    [InlineData("90", 90)]
    [InlineData("90.5", 90.5)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03", 3723)]
    [InlineData("1:02:03.25", 3723.25)]
    [InlineData("90s", 90)]
    [InlineData("2.5m", 150)]
    [InlineData("1h30m", 5400)]
    [InlineData("1h2m3s", 3723)]
    [InlineData(" 45 ", 45)]
    public void Time_AcceptsSecondsClockAndUnits(string text, double seconds)
    {
        Assert.True(ValueParsers.TryParseTime(text, out var time, out var error), error);
        Assert.Equal(TimeSpan.FromSeconds(seconds), time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:60")]
    [InlineData("1:61:00")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    [InlineData("5x")]
    [InlineData("1h30")]
    public void Time_RejectsMalformedWithMessage(string text)
    {
        Assert.False(ValueParsers.TryParseTime(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("4000", 4000)]
    [InlineData("4000k", 4000)]
    [InlineData("128K", 128)]
    [InlineData("4M", 4000)]
    [InlineData("4.5m", 4500)]
    public void BitRate_AcceptsKilobitsAndMegabits(string text, int kbps)
    {
        Assert.True(ValueParsers.TryParseBitRate(text, out var value, out _));
        Assert.Equal(kbps, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("fast")]
    [InlineData("2000M")]
    public void BitRate_RejectsInvalid(string text)
    {
        Assert.False(ValueParsers.TryParseBitRate(text, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Tracks_KeywordsAndLanguageLists()
    {
        Assert.True(ValueParsers.TryParseTracks("ALL", out var all, out _));
        Assert.IsType<TrackSelection.All>(all);
        Assert.True(ValueParsers.TryParseTracks("none", out var none, out _));
        Assert.IsType<TrackSelection.None>(none);
        Assert.True(ValueParsers.TryParseTracks("default", out var def, out _));
        Assert.IsType<TrackSelection.Default>(def);

        Assert.True(ValueParsers.TryParseTracks("jpn, en", out var languages, out _));
        Assert.Equal(["jpn", "en"], Assert.IsType<TrackSelection.Languages>(languages).Preferred.Select(l => l.Value));
    }

    [Theory]
    [InlineData("english")]
    [InlineData(",")]
    [InlineData("eng,und")]
    public void Tracks_RejectsNonLanguages(string text)
    {
        Assert.False(ValueParsers.TryParseTracks(text, out _, out var error));
        Assert.NotNull(error);
    }
}
