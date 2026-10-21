using System.Text;

namespace FluxEncoder.Core.Manifests;

/// <summary>
/// <c>${NAME}</c> substitution in manifest values (TSKE011.2). <c>${NAME:-fallback}</c> uses the fallback when the
/// variable is unset or empty, <c>$$</c> is a literal <c>$</c>, and any other <c>$</c> is kept as written.
/// </summary>
/// <remarks>
/// Substitution happens once: a value that itself contains <c>${…}</c> is not expanded again. Which names resolve is
/// the caller's decision (see <see cref="FluxfileReadOptions.Variables"/>); an unresolved name without a fallback is
/// an error rather than an empty string, so a typo cannot turn <c>${FLUX_OUT}/a.mkv</c> into <c>/a.mkv</c>.
/// </remarks>
public static class ManifestVariables
{
    public static bool TryExpand(string text, Func<string, string?> lookup, out string expanded, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lookup);

        expanded = text;
        error = null;
        if (!text.Contains('$', StringComparison.Ordinal))
        {
            return true;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '$' || i + 1 >= text.Length)
            {
                builder.Append(text[i]);
                continue;
            }

            if (text[i + 1] == '$')
            {
                builder.Append('$');
                i++;
                continue;
            }

            if (text[i + 1] != '{')
            {
                builder.Append('$');
                continue;
            }

            var close = text.IndexOf('}', i + 2);
            if (close < 0)
            {
                error = $"'${{' at character {i + 1} has no closing '}}'. Write $$ for a literal $.";
                return false;
            }

            var body = text[(i + 2)..close];
            var separator = body.IndexOf(":-", StringComparison.Ordinal);
            var name = separator < 0 ? body : body[..separator];
            if (!IsValidName(name))
            {
                error = $"'${{{body}}}' is not a variable reference; names use letters, digits, and _, and do not start with a digit.";
                return false;
            }

            var value = lookup(name);
            if (string.IsNullOrEmpty(value))
            {
                if (separator < 0)
                {
                    error = $"Variable '{name}' is not set. Pass --var {name}=VALUE, or write ${{{name}:-fallback}}.";
                    return false;
                }

                value = body[(separator + 2)..];
            }

            builder.Append(value);
            i = close;
        }

        expanded = builder.ToString();
        return true;
    }

    public static bool IsValidName(string name) =>
        name.Length > 0
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
