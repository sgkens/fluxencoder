using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FluxEncoder.Core.Media;

/// <summary>
/// An exact positive ratio such as a frame rate (<c>30000/1001</c>) or a sample aspect ratio (<c>4:3</c>).
/// </summary>
/// <remarks>
/// Values are reduced to lowest terms, so <c>60/2</c> equals <c>30/1</c>. Zero and negative ratios are
/// not representable: FFprobe reports <c>0/0</c> or <c>0:1</c> for an unknown value, which callers model as
/// <see langword="null"/>.
/// </remarks>
public readonly record struct Rational
{
    public Rational(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        var divisor = GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public long Numerator { get; }

    public long Denominator { get; }

    public double Value => (double)Numerator / Denominator;

    /// <summary>
    /// Parses <c>n/d</c> or <c>n:d</c> with invariant digits.
    /// </summary>
    /// <returns><see langword="false"/> for malformed text and for zero, negative, or unknown values.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, out Rational value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var separator = span.IndexOfAny('/', ':');
        if (separator <= 0)
        {
            return false;
        }

        const NumberStyles Digits = NumberStyles.None;
        if (!long.TryParse(span[..separator], Digits, CultureInfo.InvariantCulture, out var numerator)
            || !long.TryParse(span[(separator + 1)..], Digits, CultureInfo.InvariantCulture, out var denominator)
            || numerator <= 0
            || denominator <= 0)
        {
            return false;
        }

        value = new Rational(numerator, denominator);
        return true;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
