using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace FluxEncoder.Infrastructure.Secrets;

/// <summary>
/// Shared plumbing for credential stores reached through a command-line tool. The secret is always written to the
/// tool's standard input, never placed in its arguments, which other local users can read from the process list.
/// </summary>
public abstract class CommandSecretStore(string executable) : ISecretStore
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public abstract string Name { get; }

    protected string Executable { get; } = executable;

    public bool IsAvailable => UnavailableReason is null;

    public virtual string? UnavailableReason =>
        File.Exists(Executable) ? null : $"{Path.GetFileName(Executable)} was not found; install it or use {EnvironmentSecretStore.Prefix}<ALIAS> variables.";

    public abstract Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    public abstract Task SetAsync(string key, string secret, CancellationToken cancellationToken = default);

    public abstract Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the tool with <paramref name="arguments"/>, writing <paramref name="input"/> to its standard input.
    /// </summary>
    protected async Task<(int ExitCode, string Output, string Error)> RunAsync(IEnumerable<string> arguments, string? input, CancellationToken cancellationToken)
    {
        if (UnavailableReason is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        var startInfo = new ProcessStartInfo(Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"{Name} could not be started: {ex.Message}", ex);
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
        }

        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"{Name} did not respond (it may be waiting for an unlock prompt).");
        }

        return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
