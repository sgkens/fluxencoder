using System.Text;

namespace FluxEncoder.Infrastructure.Secrets;

/// <summary>
/// The macOS login keychain through <c>/usr/bin/security</c>. Storing uses <c>security -i</c>, which reads its command
/// from standard input, because <c>add-generic-password -w PASSWORD</c> would put the password in the process list.
/// </summary>
public sealed class KeychainStore(string executable = "/usr/bin/security") : CommandSecretStore(executable)
{
    private const string Service = "FluxEncoder";

    public override string Name => "macOS Keychain";

    public override async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var (exit, output, _) = await RunAsync(["find-generic-password", "-s", Service, "-a", key, "-w"], null, cancellationToken).ConfigureAwait(false);
        return exit == 0 && output.Length > 0 ? output.TrimEnd('\n') : null;
    }

    public override async Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        if (secret.Contains('\n', StringComparison.Ordinal) || secret.Contains('\r', StringComparison.Ordinal))
        {
            throw new ArgumentException("Keychain passwords cannot contain line breaks.", nameof(secret));
        }

        // security -i parses a shell-like line; double quotes with backslash escapes keep any password intact.
        var command = new StringBuilder("add-generic-password -U -s ")
            .Append(Quote(Service)).Append(" -a ").Append(Quote(key)).Append(" -w ").Append(Quote(secret)).Append('\n')
            .ToString();

        var (exit, _, error) = await RunAsync(["-i"], command, cancellationToken).ConfigureAwait(false);
        if (exit != 0 || error.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{Name} refused to store the secret: {error.Trim()}");
        }
    }

    public override async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var (exit, _, _) = await RunAsync(["delete-generic-password", "-s", Service, "-a", key], null, cancellationToken).ConfigureAwait(false);
        return exit == 0;
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
