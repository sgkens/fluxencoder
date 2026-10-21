namespace FluxEncoder.Infrastructure.Secrets;

/// <summary>
/// Chooses where secrets are read from and written to.
/// </summary>
/// <remarks>
/// Reading checks <c>FLUXENCODER_SECRET_*</c> environment variables first, so a service or CI job can inject secrets
/// from its own provider without touching the OS store, and then the platform credential store. Writing always goes to
/// the platform store. FluxEncoder has no fallback that writes secrets to its own files.
/// </remarks>
public static class SecretStores
{
    public static ISecretStore Platform() =>
        OperatingSystem.IsWindows() ? new WindowsCredentialStore()
        : OperatingSystem.IsMacOS() ? new KeychainStore()
        : new SecretToolStore();

    /// <summary>
    /// The key a remote alias's password is stored under.
    /// </summary>
    public static string RemoteKey(string alias) => "remote-" + alias.ToLowerInvariant();

    public static async Task<string?> ReadAsync(string key, Func<string, string?> getVariable, ISecretStore platform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (await new EnvironmentSecretStore(getVariable).GetAsync(key, cancellationToken).ConfigureAwait(false) is { } fromEnvironment)
        {
            return fromEnvironment;
        }

        return platform.IsAvailable ? await platform.GetAsync(key, cancellationToken).ConfigureAwait(false) : null;
    }
}
