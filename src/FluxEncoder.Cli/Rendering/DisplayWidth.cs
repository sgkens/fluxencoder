using System.Globalization;
using System.Text;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// How many terminal cells text occupies (TSKE008.3). <see cref="string.Length"/> counts UTF-16 code units, which is
/// wrong for every column that holds a filename or a title: <c>映画.mkv</c> is 6 code units but 8 cells, <c>é</c>
/// written as <c>e</c> plus a combining accent is 2 code units but 1 cell, and an emoji family is up to 11 code units
/// but 2 cells.
/// </summary>
/// <remarks>
/// Width is measured per grapheme cluster (what a reader sees as one character), using .NET's UAX #29 segmentation:
/// <list type="bullet">
/// <item>0 cells: a cluster that is only controls, format characters (zero-width space, joiners), or combining marks.</item>
/// <item>2 cells: East Asian Wide and Fullwidth characters (CJK, Hangul, fullwidth forms) and emoji, including any
/// cluster that asks for emoji presentation with U+FE0F.</item>
/// <item>1 cell: everything else, including East Asian Ambiguous characters such as the symbols in
/// <see cref="UiSymbols"/>, which terminals draw narrow outside CJK locales.</item>
/// </list>
/// Terminals disagree at the margins (newer emoji, ambiguous characters in CJK locales), so the result is the width most
/// terminals use, not a guarantee. Text is expected to have passed through <see cref="TerminalText"/> first.
/// </remarks>
internal static class DisplayWidth
{
    /// <summary>
    /// Inclusive code point ranges drawn two cells wide, from Unicode's EastAsianWidth.txt (W and F), the emoji blocks
    /// with default emoji presentation, and the regional indicators that pair into flags. Sorted, for binary search.
    /// </summary>
    private static readonly (int First, int Last)[] Wide =
    [
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3),
        (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F), (0x2693, 0x2693), (0x26A1, 0x26A1),
        (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5), (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA),
        (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705), (0x270A, 0x270B),
        (0x2728, 0x2728), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55), (0x2E80, 0x303E),
        (0x3041, 0x33FF), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF), (0xA960, 0xA97F), (0xAC00, 0xD7A3),
        (0xF900, 0xFAFF), (0xFE10, 0xFE19), (0xFE30, 0xFE6F), (0xFF00, 0xFF60), (0xFFE0, 0xFFE6), (0x16FE0, 0x16FE4),
        (0x17000, 0x18CFF), (0x1B000, 0x1B2FF), (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF), (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A), (0x1F1E6, 0x1F1FF), (0x1F200, 0x1F251), (0x1F300, 0x1F64F), (0x1F680, 0x1F6FF),
        (0x1F7E0, 0x1F7EB), (0x1F90C, 0x1F9FF), (0x1FA70, 0x1FAFF), (0x20000, 0x3FFFD),
    ];

    private const char EmojiPresentation = '\uFE0F';

    public static int Of(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (text.All(char.IsAsciiLetterOrDigit))
        {
            return text.Length;
        }

        var width = 0;
        var clusters = StringInfo.GetTextElementEnumerator(text);
        while (clusters.MoveNext())
        {
            width += Cluster(clusters.GetTextElement());
        }

        return width;
    }

    /// <summary>
    /// Pads with spaces to <paramref name="cells"/> cells; text already that wide or wider is returned unchanged.
    /// </summary>
    public static string PadRight(string text, int cells)
    {
        ArgumentNullException.ThrowIfNull(text);
        var width = Of(text);
        return width >= cells ? text : text + new string(' ', cells - width);
    }

    /// <summary>
    /// Shortens text to at most <paramref name="cells"/> cells by replacing its middle with
    /// <paramref name="ellipsis"/>, keeping more of the end because that is where a path's file name is. Never splits a
    /// grapheme cluster, so the result may be one cell narrower than asked when a wide character sits at the cut.
    /// </summary>
    public static string Fit(string text, int cells, string ellipsis = "\u2026")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(ellipsis);

        if (Of(text) <= cells)
        {
            return text;
        }

        var marker = Of(ellipsis);
        if (cells <= marker)
        {
            return cells <= 0 ? string.Empty : Take(ellipsis, cells, fromEnd: false);
        }

        var budget = cells - marker;
        var head = budget * 2 / 5;
        return Take(text, head, fromEnd: false) + ellipsis + Take(text, budget - head, fromEnd: true);
    }

    private static string Take(string text, int cells, bool fromEnd)
    {
        var clusters = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            clusters.Add(enumerator.GetTextElement());
        }

        if (fromEnd)
        {
            clusters.Reverse();
        }

        var taken = new List<string>();
        var used = 0;
        foreach (var cluster in clusters)
        {
            var width = Cluster(cluster);
            if (used + width > cells)
            {
                break;
            }

            taken.Add(cluster);
            used += width;
        }

        if (fromEnd)
        {
            taken.Reverse();
        }

        return string.Concat(taken);
    }

    private static int Cluster(string cluster)
    {
        if (cluster.Contains(EmojiPresentation, StringComparison.Ordinal))
        {
            return 2;
        }

        var visible = false;
        foreach (var rune in cluster.EnumerateRunes())
        {
            if (IsWide(rune.Value))
            {
                return 2;
            }

            visible |= !IsZeroWidth(rune);
        }

        return visible ? 1 : 0;
    }

    private static bool IsZeroWidth(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark;

    private static bool IsWide(int codePoint)
    {
        if (codePoint < Wide[0].First)
        {
            return false;
        }

        int low = 0, high = Wide.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (codePoint > Wide[middle].Last)
            {
                low = middle + 1;
            }
            else if (codePoint < Wide[middle].First)
            {
                high = middle - 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
