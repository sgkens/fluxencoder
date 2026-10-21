namespace FluxEncoder.Infrastructure.Secrets;

/// <summary>
/// The freedesktop Secret Service (GNOME Keyring, KWallet) on Linux, through <c>secret-tool</c> from libsecret.
/// <c>secret-tool store</c> reads the secret from standard input.
/// </summary>
public sealed class SecretToolStore(string executable = "/usr/bin/secret-tool") : CommandSecretStore(executable)
{
    private const string Application = "fluxencoder";

    public override string Name => "Secret Service (secret-tool)";

    public override async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var (exit, output, _) = await RunAsync(["lookup", "application", Application, "key", key], null, cancellationToken).ConfigureAwait(false);
        return exit == 0 && output.Length > 0 ? output.TrimEnd('\n') : null;
    }

    public override async Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        var (exit, _, error) = await RunAsync(
            ["store", "--label", $"FluxEncoder: {key}", "application", Application, "key", key],
            secret,
            cancellationToken).ConfigureAwait(false);

        if (exit != 0)
        {
            throw new InvalidOperationException($"{Name} refused to store the secret: {error.Trim()}");
        }
    }

    public override async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var (exit, _, _) = await RunAsync(["clear", "application", Application, "key", key], null, cancellationToken).ConfigureAwait(false);
        return exit == 0;
    }
}
