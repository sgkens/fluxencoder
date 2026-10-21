using System.Text;

namespace FluxEncoder.Core.Formats;

/// <summary>
/// Escapes untrusted text for one Markdown table cell (TSKE011.5), so a title such as <c>a | b</c>, <c>**x**</c>,
/// <c>[link](https://…)</c>, or <c>&lt;script&gt;</c> renders as those literal characters.
/// </summary>
/// <remarks>
/// Backslash escapes cover every character that can start inline markup in CommonMark and GitHub Flavored Markdown:
/// emphasis, code, links and images, raw HTML, entities, strikethrough, math, and the cell separator. Line breaks
/// would end the table row, so they become spaces; other control characters are dropped. Characters that only matter
/// at the start of a block (<c>#</c>, <c>-</c>, <c>+</c>, digits) are inert inside a cell, but <c>#</c> is escaped
/// anyway because some renderers link headings and issue references.
/// </remarks>
public static class MarkdownText
{
    private const string Special = "\\`*_{}[]()<>#!|~&$";

    public static string Cell(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (Special.Contains(c, StringComparison.Ordinal))
            {
                builder.Append('\\').Append(c);
            }
            else if (c is '\n' or '\r' or '\t' or '\u2028' or '\u2029')
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
