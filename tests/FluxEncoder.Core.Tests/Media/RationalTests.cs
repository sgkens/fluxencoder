using System.Globalization;
using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Tests.Media;

public sealed class RationalTests
{
    [Theory]
    [InlineData("30000/1001", 30000, 1001)]
    [InlineData("25/1", 25, 1)]
    [InlineData("60/2", 30, 1)]
    [InlineData("4:3", 4, 3)]
    [InlineData(" 16:9 ", 16, 9)]
    public void TryParse_AcceptsSlashAndColonForms(string text, long numerator, long denominator)
    {
        Assert.True(Rational.TryParse(text, out var value));
        Assert.Equal(numerator, value.Numerator);
        Assert.Equal(denominator, value.Denominator);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0/0")]
    [InlineData("0:1")]
    [InlineData("1/0")]
    [InlineData("-1/2")]
    [InlineData("+1/2")]
    [InlineData("1.5/2")]
    [InlineData("1,000/1")]
    [InlineData("30")]
    [InlineData("/30")]
    [InlineData("30/")]
    [InlineData("a/b")]
    [InlineData("1/2/3")]
    public void TryParse_RejectsMalformedOrUnknown(string? text)
    {
        Assert.False(Rational.TryParse(text, out _));
    }

    [Fact]
    public void Equality_ComparesReducedValues()
    {
        Assert.Equal(new Rational(30, 1), new Rational(60, 2));
        Assert.NotEqual(new Rational(30000, 1001), new Rational(30, 1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Constructor_RejectsNonPositiveTerms(long numerator, long denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rational(numerator, denominator));
    }

    [Fact]
    public void ToString_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal("30000/1001", new Rational(30000, 1001).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Value_IsTheQuotient()
    {
        Assert.Equal(29.97, new Rational(30000, 1001).Value, precision: 2);
    }
}
