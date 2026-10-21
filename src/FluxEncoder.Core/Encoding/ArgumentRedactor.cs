using System.Text;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Masks secrets in FFmpeg arguments before they are shown, logged, or written to JSON. The real arguments
/// passed to the process are never altered.
/// </summary>
/// <remarks>
/// Covers URL credentials (<c>rtmp://user:pass@host</c>), secret-looking query parameters
/// (<c>?token=…</c>), RTMP stream keys (the last path segment), and values of options that carry secrets
/// (<c>-headers</c>, <c>-passphrase</c>, encryption keys). Redaction is conservative: it may mask a harmless
/// value, but it must never reveal a secret.
/// </remarks>
public static class ArgumentRedactor
{
    public const string Mask = "***";

    private static readonly HashSet<string> SecretOptions = new(StringComparer.Ordinal)
    {
        "-headers",
        "-cookies",
        "-passphrase",
        "-auth_type",
        "-key",
        "-iv",
        "-decryption_key",
        "-encryption_key",
        "-encryption_kid",
        "-cenc_key",
        "-cenc_kid",
        "-hls_enc_key",
        "-hls_enc_iv",
        "-srtp_out_params",
        "-srtp_in_params",
        "-icy_metadata_headers",
    };

    private static readonly string[] SecretQueryNames =
        ["key", "token", "auth", "password", "pass", "pwd", "secret", "signature", "sig", "access_token", "api_key", "apikey", "streamkey", "stream_key", "credential"];

    private static readonly HashSet<string> StreamKeySchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rtmp", "rtmps", "rtmpt", "rtmpe", "rtmpte", "rtmpts",
    };

    public static IReadOnlyList<string> Redact(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var redacted = new string[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var previous = i > 0 ? arguments[i - 1] : null;
            redacted[i] = previous is not null && SecretOptions.Contains(StripStreamSpecifier(previous))
                ? Mask
                : RedactUrl(arguments[i]);
        }

        return redacted;
    }

    /// <summary>
    /// Masks credentials, secret query values, and RTMP stream keys in a URL; other text is returned unchanged.
    /// </summary>
    public static string RedactUrl(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            return value;
        }

        var scheme = value[..schemeEnd];
        var rest = value[(schemeEnd + 3)..];

        var fragmentStart = rest.IndexOf('#');
        var fragment = fragmentStart >= 0 ? rest[fragmentStart..] : string.Empty;
        if (fragmentStart >= 0)
        {
            rest = rest[..fragmentStart];
        }

        var queryStart = rest.IndexOf('?');
        var query = queryStart >= 0 ? rest[(queryStart + 1)..] : null;
        var authorityAndPath = queryStart >= 0 ? rest[..queryStart] : rest;

        var pathStart = authorityAndPath.IndexOf('/');
        var authority = pathStart >= 0 ? authorityAndPath[..pathStart] : authorityAndPath;
        var path = pathStart >= 0 ? authorityAndPath[pathStart..] : string.Empty;

        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            var userInfo = authority[..at];
            var colon = userInfo.IndexOf(':');
            authority = (colon >= 0 ? userInfo[..colon] + ":" + Mask : Mask) + authority[at..];
        }

        if (StreamKeySchemes.Contains(scheme))
        {
            path = RedactLastSegment(path);
        }

        var builder = new StringBuilder(value.Length);
        builder.Append(scheme).Append("://").Append(authority).Append(path);

        if (query is not null)
        {
            builder.Append('?').Append(RedactQuery(query));
        }

        return builder.Append(fragment).ToString();
    }

    private static string RedactLastSegment(string path)
    {
        // rtmp://host/app/streamkey: two or more segments means the last one is the key.
        var segments = path.Split('/');
        var nonEmpty = segments.Count(s => s.Length > 0);
        if (nonEmpty < 2)
        {
            return path;
        }

        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (segments[i].Length > 0)
            {
                segments[i] = Mask;
                break;
            }
        }

        return string.Join('/', segments);
    }

    private static string RedactQuery(string query) =>
        string.Join('&', query.Split('&').Select(pair =>
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                return pair;
            }

            var name = pair[..equals];
            return SecretQueryNames.Any(secret => name.Contains(secret, StringComparison.OrdinalIgnoreCase))
                ? name + "=" + Mask
                : pair;
        }));

    private static string StripStreamSpecifier(string option)
    {
        var colon = option.IndexOf(':');
        return colon > 0 ? option[..colon] : option;
    }
}
