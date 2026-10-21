using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Security;

/// <summary>
/// Masks secrets in free text before it leaves FluxEncoder (TSKE010.4): terminal output, JSON, history, and crash
/// reports. Applied at those boundaries rather than where messages are built, so a message composed anywhere,
/// including FFmpeg's own error output, cannot carry a secret out.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Every URL in the text gets <see cref="ArgumentRedactor.RedactUrl"/>: passwords, secret-named query values,
/// and RTMP stream keys.</item>
/// <item><c>Authorization:</c> and <c>Proxy-Authorization:</c> header values are masked.</item>
/// <item>Secrets FluxEncoder itself resolves (credential-store passwords for <c>remote://</c> sources) are registered
/// with <see cref="Register"/> and masked wherever they appear, in any form.</item>
/// </list>
/// Redaction cannot recognise a secret it was never told about and that has no recognisable shape; the threat model
/// (<c>docs/security.md</c>) lists what remains visible.
/// </remarks>
public static partial class Redaction
{
    private const int MinimumSecretLength = 4;

    private static readonly ConcurrentDictionary<string, byte> Secrets = new(StringComparer.Ordinal);

    /// <summary>
    /// Masks <paramref name="secret"/> in all later output from this process. Values shorter than four characters
    /// are ignored, since masking them would garble ordinary text.
    /// </summary>
    public static void Register(string? secret)
    {
        if (secret is { Length: >= MinimumSecretLength })
        {
            Secrets.TryAdd(secret, 0);
            var escaped = Uri.EscapeDataString(secret);
            if (escaped != secret)
            {
                Secrets.TryAdd(escaped, 0);
            }
        }
    }

    public static string Text(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = UrlPattern().Replace(text, match => ArgumentRedactor.RedactUrl(match.Value));
        result = AuthorizationPattern().Replace(result, match => match.Groups["name"].Value + ArgumentRedactor.Mask);

        foreach (var secret in Secrets.Keys.OrderByDescending(s => s.Length))
        {
            result = result.Replace(secret, ArgumentRedactor.Mask, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Forgets registered secrets. For tests.
    /// </summary>
    public static void Reset() => Secrets.Clear();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://[^\s'""<>]+")]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?<name>(?:Proxy-)?Authorization:\s*)[^\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationPattern();
}
