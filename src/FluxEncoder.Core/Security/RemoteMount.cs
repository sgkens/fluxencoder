using System.Collections.Frozen;
using System.Text;

namespace FluxEncoder.Core.Security;

/// <summary>
/// A named remote location (<c>remote://ALIAS/path</c>). Holds no secret: the password lives in the OS credential store
/// or an environment variable and is supplied only when a URL is built (TSKE010.3).
/// </summary>
public sealed record RemoteMount
{
    private static readonly FrozenSet<string> Protocols = new[] { "ftp", "sftp", "smb", "http", "https" }.ToFrozenSet(StringComparer.Ordinal);

    public RemoteMount(string alias, string protocol, string host, int? port = null, string? user = null, string? basePath = null)
    {
        if (!IsValidAlias(alias))
        {
            throw new ArgumentException("An alias is 1 to 64 letters, digits, '-' or '_'.", nameof(alias));
        }

        protocol = protocol?.ToLowerInvariant() ?? string.Empty;
        if (!Protocols.Contains(protocol))
        {
            throw new ArgumentException($"Remote protocol must be one of {string.Join(", ", Protocols.Order(StringComparer.Ordinal))}.", nameof(protocol));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (host.IndexOfAny(['/', '@', '?', '#', ' ', '\\']) >= 0)
        {
            throw new ArgumentException("The host must be a bare name or address.", nameof(host));
        }

        if (port is { } p)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(p, 1, nameof(port));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(p, 65535, nameof(port));
        }

        Alias = alias;
        Protocol = protocol;
        Host = host;
        Port = port;
        User = string.IsNullOrEmpty(user) ? null : user;
        BasePath = basePath?.Trim().Trim('/') is { Length: > 0 } trimmed ? trimmed : null;
    }

    public string Alias { get; }

    public string Protocol { get; }

    public string Host { get; }

    public int? Port { get; }

    public string? User { get; }

    /// <summary>
    /// Path on the remote that <c>remote://ALIAS/</c> starts from, without leading or trailing slashes.
    /// </summary>
    public string? BasePath { get; }

    /// <summary>
    /// Reads a mount from a URL such as <c>sftp://user@nas.local:22/media</c>.
    /// </summary>
    /// <returns>The mount, or an error. A URL that carries a password is refused: passwords belong in the credential
    /// store, never in settings.</returns>
    public static (RemoteMount? Mount, string? Error) Parse(string alias, string url)
    {
        if (!IsValidAlias(alias))
        {
            return (null, $"'{alias}' is not a valid alias: use 1 to 64 letters, digits, '-' or '_'.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return (null, $"'{Redaction.Text(url)}' is not a URL like sftp://user@host/path.");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length == 2)
        {
            return (null, "Do not put the password in the URL; it would be stored in plain text. Use --password-stdin.");
        }

        try
        {
            var mount = new RemoteMount(
                alias,
                uri.Scheme,
                uri.Host,
                uri.IsDefaultPort ? null : uri.Port,
                userInfo[0].Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : null,
                Uri.UnescapeDataString(uri.AbsolutePath));
            return (mount, null);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// The mount as a URL without any password, for settings and listings.
    /// </summary>
    public string ToDisplayUrl()
    {
        var url = new StringBuilder(Protocol).Append("://");
        if (User is not null)
        {
            url.Append(Uri.EscapeDataString(User)).Append('@');
        }

        url.Append(Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host);
        if (Port is { } port)
        {
            url.Append(':').Append(port);
        }

        foreach (var segment in BasePath?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            url.Append('/').Append(Uri.EscapeDataString(segment));
        }

        return url.ToString();
    }

    /// <summary>
    /// Splits <c>remote://ALIAS/path</c> into its alias and path, or returns <see langword="null"/> for other inputs.
    /// </summary>
    public static (string Alias, string Path)? SplitRemoteInput(string input)
    {
        const string Prefix = "remote://";
        if (input is null || !input.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = input[Prefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        return slash <= 0 ? (rest, string.Empty) : (rest[..slash], rest[(slash + 1)..]);
    }

    public static bool IsValidAlias(string? alias) =>
        alias is { Length: >= 1 and <= 64 } && alias.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// The URL FFmpeg opens for <paramref name="relativePath"/>. The user name, the password, and every path segment
    /// are percent-encoded, so characters such as <c>@</c>, <c>:</c>, <c>#</c>, <c>?</c>, and spaces cannot change
    /// which host, user, or file the URL names (TSKE010.4).
    /// </summary>
    /// <exception cref="ArgumentException">The path tries to leave the base path (<c>..</c>) or is empty.</exception>
    public string ToUrl(string relativePath, string? password)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("A remote path must name a file.", nameof(relativePath));
        }

        if (segments.Any(s => s is "." or ".."))
        {
            throw new ArgumentException("A remote path cannot contain '.' or '..' segments.", nameof(relativePath));
        }

        var url = new StringBuilder();
        url.Append(Protocol).Append("://");

        if (User is not null)
        {
            url.Append(Uri.EscapeDataString(User));
            if (!string.IsNullOrEmpty(password))
            {
                url.Append(':').Append(Uri.EscapeDataString(password));
            }

            url.Append('@');
        }

        url.Append(Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host);
        if (Port is { } port)
        {
            url.Append(':').Append(port);
        }

        foreach (var segment in (BasePath?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? []).Concat(segments))
        {
            url.Append('/').Append(Uri.EscapeDataString(segment));
        }

        return url.ToString();
    }
}
