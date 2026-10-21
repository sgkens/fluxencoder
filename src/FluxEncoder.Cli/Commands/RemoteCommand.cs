using System.CommandLine;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;
using FluxEncoder.Infrastructure.Configuration;
using FluxEncoder.Infrastructure.Secrets;

namespace FluxEncoder.Cli.Commands;

/// <summary>
/// <c>remote</c>: named remote locations for <c>remote://ALIAS/path</c> inputs (TSKE010.3). The location goes in
/// <c>settings.json</c>; the password goes only to the OS credential store, read from standard input so it never appears
/// in shell history or the process list.
/// </summary>
internal static class RemoteCommand
{
    public static Command Build(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var alias = new Argument<string>("alias") { Description = "Name used as remote://ALIAS/path." };
        var url = new Argument<string>("url") { Description = "Location without a password, such as sftp://user@nas.local/media." };
        var passwordStdin = new Option<bool>("--password-stdin") { Description = "Read the password from standard input and keep it in the OS credential store." };

        var add = new Command("add", "Add or replace a remote.") { alias, url, passwordStdin };
        add.SetAction(async (result, token) =>
        {
            var (mount, error) = RemoteMount.Parse(result.GetValue(alias)!, result.GetValue(url)!);
            if (mount is null)
            {
                await host.Error.WriteLineAsync(TerminalText.Safe(error)).ConfigureAwait(false);
                return (int)ExitCode.InvalidUsage;
            }

            if (result.GetValue(passwordStdin))
            {
                var password = (await host.In.ReadToEndAsync(token).ConfigureAwait(false)).TrimEnd('\r', '\n');
                if (password.Length == 0)
                {
                    await host.Error.WriteLineAsync("No password was given on standard input.").ConfigureAwait(false);
                    return (int)ExitCode.InvalidUsage;
                }

                var store = host.SecretStore();
                if (!store.IsAvailable)
                {
                    await host.Error.WriteLineAsync($"No credential store is available: {store.UnavailableReason}").ConfigureAwait(false);
                    return (int)ExitCode.DependencyMissing;
                }

                try
                {
                    await store.SetAsync(SecretStores.RemoteKey(mount.Alias), password, token).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    await host.Error.WriteLineAsync(TerminalText.Safe(ex.Message)).ConfigureAwait(false);
                    return (int)ExitCode.GeneralFailure;
                }
            }

            var (settings, warnings) = SettingsStore.Load(host.Paths.SettingsFile);
            foreach (var warning in warnings)
            {
                await host.Error.WriteLineAsync(TerminalText.Safe(warning)).ConfigureAwait(false);
            }

            SettingsStore.Save(host.Paths.SettingsFile, settings with { Remotes = settings.Remotes.SetItem(mount.Alias, mount) });
            // Display URLs never contain a password, and redaction would also mask the bare user name.
            await host.Out.WriteLineAsync(TerminalText.Clean($"Added remote '{mount.Alias}' -> {mount.ToDisplayUrl()}")).ConfigureAwait(false);
            return (int)ExitCode.Success;
        });

        var list = new Command("list", "Show remotes and where each password comes from (never the password itself).");
        list.SetAction(async (_, token) =>
        {
            var (settings, _) = SettingsStore.Load(host.Paths.SettingsFile);
            if (settings.Remotes.IsEmpty)
            {
                await host.Error.WriteLineAsync("No remotes. Add one with: remote add NAME URL --password-stdin").ConfigureAwait(false);
                return (int)ExitCode.Success;
            }

            var store = host.SecretStore();
            foreach (var (name, mount) in settings.Remotes.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
            {
                var key = SecretStores.RemoteKey(name);
                var source = host.GetVariable(EnvironmentSecretStore.VariableName(key)) is { Length: > 0 }
                    ? "password from environment"
                    : store.IsAvailable && await TryHasAsync(store, key, token).ConfigureAwait(false) ? $"password in {store.Name}" : "no password stored";
                await host.Out.WriteLineAsync(TerminalText.Clean($"{name}  {mount.ToDisplayUrl()}  ({source})")).ConfigureAwait(false);
            }

            return (int)ExitCode.Success;
        });

        var removeAlias = new Argument<string>("alias") { Description = "The remote to remove." };
        var remove = new Command("remove", "Remove a remote and its stored password.") { removeAlias };
        remove.SetAction(async (result, token) =>
        {
            var name = result.GetValue(removeAlias)!;
            var (settings, _) = SettingsStore.Load(host.Paths.SettingsFile);
            if (!settings.Remotes.ContainsKey(name))
            {
                await host.Error.WriteLineAsync(TerminalText.Safe($"There is no remote '{name}'.")).ConfigureAwait(false);
                return (int)ExitCode.InvalidUsage;
            }

            SettingsStore.Save(host.Paths.SettingsFile, settings with { Remotes = settings.Remotes.Remove(name) });

            var store = host.SecretStore();
            if (store.IsAvailable)
            {
                try
                {
                    await store.DeleteAsync(SecretStores.RemoteKey(name), token).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    await host.Error.WriteLineAsync(TerminalText.Safe($"The remote was removed, but its password could not be deleted: {ex.Message}")).ConfigureAwait(false);
                }
            }

            await host.Out.WriteLineAsync(TerminalText.Safe($"Removed remote '{name}'.")).ConfigureAwait(false);
            return (int)ExitCode.Success;
        });

        return new Command("remote", "Manage remote://ALIAS locations; passwords are kept in the OS credential store.") { add, list, remove };
    }

    private static async Task<bool> TryHasAsync(ISecretStore store, string key, CancellationToken token)
    {
        try
        {
            return await store.GetAsync(key, token).ConfigureAwait(false) is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
