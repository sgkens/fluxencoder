using System.Text;

namespace FluxEncoder.Infrastructure.Secrets;

/// <summary>
/// Read-only secrets from environment variables, the hand-off point for external secret providers (CI secrets,
/// Kubernetes secrets, vault agents): <c>FLUXENCODER_SECRET_&lt;KEY&gt;</c>, with the key upper-cased and every character
/// other than a letter or digit replaced by <c>_</c>.
/// </summary>
public sealed class EnvironmentSecretStore(Func<string, string?> getVariable) : ISecretStore
{
    public const string Prefix = "FLUXENCODER_SECRET_";

    public string Name => "environment variables";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public static string VariableName(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var builder = new StringBuilder(Prefix, Prefix.Length + key.Length);
        foreach (var c in key)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }

        return builder.ToString();
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(getVariable(VariableName(key)) is { Length: > 0 } value ? value : null);

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Environment secrets are set by the environment; export {VariableName(key)} instead.");

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
