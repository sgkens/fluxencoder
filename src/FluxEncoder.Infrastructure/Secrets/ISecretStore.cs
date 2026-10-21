namespace FluxEncoder.Infrastructure.Secrets;

/// <summary>
/// Where remote passwords are kept (TSKE010.3). Implementations never write secrets to FluxEncoder's own files and
/// never pass them on a command line, where other local processes could read them.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// A name for messages, such as "Windows Credential Manager".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether the store can be used here; when not, <see cref="UnavailableReason"/> says why.
    /// </summary>
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string secret, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}
