using System.Text;

namespace FluxEncoder.Core.Formats;

/// <summary>
/// Encodes untrusted text for HTML and SVG (TSKE011.5). File names, titles, and tags come from media files and can
/// contain markup; encoded, they always display as text and never become elements, attributes, or scripts.
/// </summary>
/// <remarks>
/// The same encoding is safe in element content and in double- or single-quoted attribute values. Characters XML
/// forbids (C0 controls other than tab, line feed, and carriage return, unpaired surrogates, U+FFFE, U+FFFF) become
/// U+FFFD, so an SVG or XHTML document stays well-formed whatever the input.
/// </remarks>
public static class MarkupText
{
    public static string Encode(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&#39;"); break;
                case '\t' or '\n' or '\r': builder.Append(c); break;
                case < ' ' or '\uFFFE' or '\uFFFF': builder.Append('\uFFFD'); break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        builder.Append(c).Append(text[++i]);
                    }
                    else
                    {
                        builder.Append(char.IsSurrogate(c) ? '\uFFFD' : c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
