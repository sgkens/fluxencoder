using System.Text;
using FluxEncoder.Core.Security;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// Makes untrusted text safe to print to a terminal (TSKE010.7). Filenames, media metadata, URLs, and FFmpeg's own
/// messages can contain control characters; written raw, an escape sequence in a filename could recolour, move, or
/// rewrite what the user sees, or change the terminal's title or clipboard.
/// </summary>
/// <remarks>
/// Replaced with <c>?</c>: C0 controls except line feed (tab becomes a space), DEL, C1 controls (U+0080–U+009F, which
/// some terminals treat as escape introducers), and the Unicode bidirectional formatting characters, which can make a
/// name display in a different order from how it is stored ("Trojan Source" spoofing). JSON output needs none of this:
/// the JSON writer escapes control characters itself.
/// </remarks>
internal static class TerminalText
{
    /// <summary>
    /// Masks secrets (<see cref="Redaction.Text"/>) and then neutralizes control characters: the form every human-
    /// readable line takes on its way out.
    /// </summary>
    public static string Safe(string? text) => Clean(Redaction.Text(text));

    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Any(IsUnsafe))
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '\t' => ' ',
                _ when IsUnsafe(c) => '?',
                _ => c,
            });
        }

        return builder.ToString();
    }

    private static bool IsUnsafe(char c) =>
        (c < 0x20 && c != '\n')
        || c is >= '\u007F' and <= '\u009F'
        || c is >= '‪' and <= '‮'
        || c is >= '⁦' and <= '⁩'
        || c is '‎' or '‏' or '؜';
}
