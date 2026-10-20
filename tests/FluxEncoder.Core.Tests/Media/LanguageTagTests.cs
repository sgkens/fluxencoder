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

    [Fact]
    public void Default_ToStringIsEmpty()
    {
        Assert.Equal(string.Empty, default(LanguageTag).ToString());
    }
}
