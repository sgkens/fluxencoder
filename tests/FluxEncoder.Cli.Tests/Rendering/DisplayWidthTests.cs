using FluxEncoder.Cli.Rendering;

namespace FluxEncoder.Cli.Tests.Rendering;

public sealed class DisplayWidthTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("clip.mkv", 8)]
    [InlineData("映画.mkv", 8)]                                   // CJK: two cells each
    [InlineData("한국어", 6)]                                 // Hangul syllables
    [InlineData("café", 4)]                                    // e + combining acute is one cell
    [InlineData("ＡＢ", 4)]                                       // fullwidth A B
    [InlineData("a​b", 2)]                                           // zero-width space
    [InlineData("🎬", 2)]                                       // clapper board emoji
    [InlineData("👨‍👩‍👧", 2)]   // family: one ZWJ cluster
    [InlineData("🇯🇵", 2)]                           // flag: regional indicator pair
    [InlineData("❤️", 2)]                                       // heart with emoji presentation
    [InlineData("❤", 1)]                                             // heart as text
    [InlineData("▶ ■ → …", 7)]                        // the UI symbols stay narrow
    public void Of_CountsTerminalCells(string text, int expected)
    {
        Assert.Equal(expected, DisplayWidth.Of(text));
    }

    [Fact]
    public void PadRight_PadsByCellsNotCodeUnits()
    {
        Assert.Equal("映画  ", DisplayWidth.PadRight("映画", 6));
        Assert.Equal("café  ", DisplayWidth.PadRight("café", 6));
        Assert.Equal("toolong", DisplayWidth.PadRight("toolong", 3));
    }

    [Fact]
    public void Fit_LeavesShortTextAlone()
    {
        Assert.Equal("clip.mkv", DisplayWidth.Fit("clip.mkv", 8));
    }

    [Fact]
    public void Fit_ShortensTheMiddleAndKeepsTheFileName()
    {
        var fitted = DisplayWidth.Fit("/media/archive/2024/holiday/final-cut.mkv", 24);

        Assert.Equal(24, DisplayWidth.Of(fitted));
        Assert.StartsWith("/media/", fitted, StringComparison.Ordinal);
        Assert.EndsWith("final-cut.mkv", fitted, StringComparison.Ordinal);
        Assert.Contains('…', fitted);
    }

    [Fact]
    public void Fit_NeverSplitsWideCharactersOrClusters()
    {
        var text = string.Concat(Enumerable.Repeat("映👨‍👩é", 10));

        for (var cells = 0; cells <= DisplayWidth.Of(text); cells++)
        {
            var fitted = DisplayWidth.Fit(text, cells);
            Assert.True(DisplayWidth.Of(fitted) <= cells, $"{cells}: '{fitted}' is {DisplayWidth.Of(fitted)} cells.");
            Assert.True(IsWellFormed(fitted), $"{cells}: a surrogate pair was split.");
            Assert.False(fitted.Length > 0 && fitted[^1] == '‍', "A joiner was cut from its cluster.");
        }

        static bool IsWellFormed(string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i++;
                }
                else if (char.IsSurrogate(s[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    [Fact]
    public void Fit_UsesTheGivenEllipsis()
    {
        Assert.Equal("ab...wxyz0", DisplayWidth.Fit("abcdefghijklmnopqrstuvwxyz0", 10, "..."));
        Assert.Equal("..", DisplayWidth.Fit("abcdefgh", 2, "..."));
        Assert.Equal(string.Empty, DisplayWidth.Fit("abcdefgh", 0));
    }
}
