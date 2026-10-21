using System.Globalization;

namespace FluxEncoder.Core.Formats;

/// <summary>
/// A two-part "label | value" badge as a self-contained SVG (TSKE011.5): no scripts, no event attributes, no links,
/// no external fonts or images, and both texts encoded with <see cref="MarkupText"/>. Safe to embed in a README or to
/// serve from a web page.
/// </summary>
public static class SvgBadge
{
    private const int Height = 20;
    private const int Padding = 6;
    private const int MaxCharacters = 64;

    public static string Render(string label, string value)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);

        label = Shorten(label);
        value = Shorten(value);
        var labelWidth = TextWidth(label) + (2 * Padding);
        var valueWidth = TextWidth(value) + (2 * Padding);
        var width = labelWidth + valueWidth;

        string N(double number) => number.ToString("0.#", CultureInfo.InvariantCulture);

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{N(width)}" height="{Height}" role="img" aria-label="{MarkupText.Encode(label)}: {MarkupText.Encode(value)}">
              <title>{MarkupText.Encode(label)}: {MarkupText.Encode(value)}</title>
              <rect width="{N(labelWidth)}" height="{Height}" fill="#555"/>
              <rect x="{N(labelWidth)}" width="{N(valueWidth)}" height="{Height}" fill="#2b6cb0"/>
              <g fill="#fff" font-family="DejaVu Sans,Verdana,Geneva,sans-serif" font-size="11" text-anchor="middle">
                <text x="{N(labelWidth / 2)}" y="14">{MarkupText.Encode(label)}</text>
                <text x="{N(labelWidth + (valueWidth / 2))}" y="14">{MarkupText.Encode(value)}</text>
              </g>
            </svg>

            """;
    }

    /// <summary>
    /// Long values would make an unusable badge; anything past <see cref="MaxCharacters"/> is cut with an ellipsis.
    /// </summary>
    private static string Shorten(string text)
    {
        var flat = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= MaxCharacters ? flat : string.Concat(flat.AsSpan(0, MaxCharacters - 1), "\u2026");
    }

    /// <summary>
    /// An estimate for an 11px sans-serif face; badges need only look balanced, not measure exactly.
    /// </summary>
    private static double TextWidth(string text) => Math.Ceiling(text.Sum(c => c switch
    {
        'i' or 'j' or 'l' or '.' or ',' or ':' or ';' or '|' or '!' or '\'' or ' ' => 3.5,
        'm' or 'w' or 'M' or 'W' => 10.0,
        >= 'A' and <= 'Z' => 8.0,
        > '\u2E80' => 11.0,
        _ => 6.5,
    }));
}
