using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Tests.Media;

public sealed class LanguageTagTests
{
    [Theory]
    [InlineData("eng", "eng", "eng")]
    [InlineData("ENG", "eng", "eng")]
    [InlineData("en", "en", "en")]
    [InlineData("pt-BR", "pt-BR", "pt")]
    [InlineData("ZH-Hant", "zh-Hant", "zh")]
    [InlineData(" fre ", "fre", "fre")]
    public void TryParse_NormalizesPrimarySubtag(string text, string value, string primary)
    {
        Assert.True(LanguageTag.TryParse(text, out var tag));
        Assert.Equal(value, tag.Value);
        Assert.Equal(primary, tag.PrimarySubtag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("und")]
    [InlineData("UND")]
    [InlineData("e")]
    [InlineData("engl")]
    [InlineData("e1g")]
    [InlineData("en-")]
    [InlineData("en--US")]
    [InlineData("en-toolongsub")]
    [InlineData("én")]
    public void TryParse_RejectsUndeterminedOrMalformed(string? text)
    {
        Assert.False(LanguageTag.TryParse(text, out _));
    }

    [Fact]
    public void Equality_IgnoresInputCaseOfPrimarySubtag()
    {
        Assert.True(LanguageTag.TryParse("ENG", out var upper));
        Assert.True(LanguageTag.TryParse("eng", out var lower));
        Assert.Equal(lower, upper);
    }

    [Theory]
    [InlineData("en", "eng", true)]
    [InlineData("eng", "en", true)]
    [InlineData("fre", "fra", true)]
    [InlineData("fr", "fre", true)]
    [InlineData("ger", "de", true)]
    [InlineData("chi", "zho", true)]
    [InlineData("pt", "pt-BR", true)]
    [InlineData("pt", "por", true)]
    [InlineData("pt-BR", "pt-br", true)]
    [InlineData("pt-BR", "por-BR", true)]
    [InlineData("pt-BR", "pt-PT", false)]
    [InlineData("pt-BR", "por", false)]
    [InlineData("eng", "spa", false)]
    [InlineData("nb", "no", false)]
    [InlineData("tlh", "tlh", true)]
    public void Matches_TreatsIsoCodeFormsAsEquivalent(string requested, string stream, bool expected)
    {
        Assert.True(LanguageTag.TryParse(requested, out var request));
        Assert.True(LanguageTag.TryParse(stream, out var streamTag));

        Assert.Equal(expected, request.Matches(streamTag));
    }

    [Fact]
    public void Matches_DefaultTagMatchesNothing()
    {
        Assert.True(LanguageTag.TryParse("eng", out var eng));

        Assert.False(default(LanguageTag).Matches(eng));
        Assert.False(eng.Matches(default));
    }

    [Fact]
    public void Default_ToStringIsEmpty()
    {
        Assert.Equal(string.Empty, default(LanguageTag).ToString());
    }
}
