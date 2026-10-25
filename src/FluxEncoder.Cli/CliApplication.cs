using System.CommandLine;
using System.Runtime.InteropServices;
using FluxEncoder.Cli.Commands;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Configuration;

namespace FluxEncoder.Cli;

/// <summary>
/// Builds the single command tree for the CLI and runs it. Commands are registered here only once they are
/// implemented and covered by tests (TSKE007.8); experimental ones say so in their description.
/// </summary>
/// <remarks>
/// Exit codes follow <see cref="ExitCode"/>: parse and validation errors return 2 (<see cref="ExitCode.InvalidUsage"/>)
/// rather than System.CommandLine's default of 1, and an interrupted run returns 130. SIGINT (Ctrl+C) and SIGTERM are
/// handled here: the first signal cancels gracefully (FFmpeg finalizes through <c>q</c> and partial output is cleaned
/// up), a second one ends the process immediately.
/// </remarks>
public static class CliApplication
{
    public static RootCommand BuildRootCommand() => BuildRootCommand(CliHost.Console, new CliSession());

    internal static RootCommand BuildRootCommand(CliHost host, CliSession session)
    {
        var root = new RootCommand("FluxEncoder: a terminal front end for FFmpeg encoding jobs.")
        {
            EncodeCommand.Build(host, session),
            MergeCommand.Build(host, session),
            ConfigCommand.Build(host),
            HistoryCommand.Build(host),
            RemoteCommand.Build(host),
            ApplyCommand.Build(host, session),
            InfoCommand.Build(host),
        };

        return root;
    }

    public static int Run(string[] args)
    {
        using var cancellation = new CancellationTokenSource();

        void OnSignal(PosixSignalContext context)
        {
            // First signal: stop gracefully. A second one falls through to the default action and ends the process.
            if (!cancellation.IsCancellationRequested)
            {
                context.Cancel = true;
                cancellation.Cancel();
            }
        }

        // SIGINT is Ctrl+C on every platform (including Windows); SIGTERM is what kill, systemd, and container
        // runtimes send. Both get the same graceful path.
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);

        return RunAsync(args, CliHost.Console, cancellation.Token).GetAwaiter().GetResult();
    }

    internal static async Task<int> RunAsync(string[] args, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(host);

        var session = new CliSession();
        var root = BuildRootCommand(host, session);
        var parse = root.Parse(args);

        if (parse.Errors.Count == 0 && parse.CommandResult.Command.Name == EncodeCommand.Name)
        {
            var useSettings = !parse.GetValue<bool>("--no-config");
            var preset = parse.GetValue<string?>("--preset");
            if (useSettings || preset is not null)
            {
                var (settings, warnings) = SettingsStore.Load(host.Paths.SettingsFile);
                session.Settings = settings;
                session.Notices.AddRange(warnings);
            }

            var merged = SettingsMerge.Apply(parse, parse.CommandResult.Command, args, session.Settings, preset, useSettings);
            if (merged.Error is { } mergeError)
            {
                return await UsageErrorAsync(host, [mergeError]).ConfigureAwait(false);
            }

            session.Notices.AddRange(merged.Notices);
            if (merged.Arguments.Count > args.Length)
            {
                parse = root.Parse(merged.Arguments);
                if (parse.Errors.Count > 0)
                {
                    return await UsageErrorAsync(
                        host,
                        [.. parse.Errors.Select(e => e.Message), $"Some values came from settings: {string.Join(", ", merged.Provenance)}."]).ConfigureAwait(false);
                }
            }
        }

        if (parse.Errors.Count > 0)
        {
            return await UsageErrorAsync(host, [.. parse.Errors.Select(e => e.Message)]).ConfigureAwait(false);
        }

        try
        {
            var exitCode = await parse.InvokeAsync(
                new InvocationConfiguration
                {
                    Output = host.Out,
                    Error = host.Error,
                    EnableDefaultExceptionHandler = false,
                    ProcessTerminationTimeout = null,
                },
                cancellationToken).ConfigureAwait(false);

            return cancellationToken.IsCancellationRequested && exitCode == (int)ExitCode.Success ? (int)ExitCode.Cancelled : exitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (int)ExitCode.Cancelled;
        }
        catch (Exception ex)
        {
            // Leave the terminal usable before reporting: restore colours and show the cursor a progress display hid.
            if (host.ErrorIsTerminal)
            {
                Console.ResetColor();
                await host.Error.WriteAsync("\u001b[?25h").ConfigureAwait(false);
            }

            await host.Error.WriteLineAsync(TerminalText.Safe($"Internal error: {ex.Message}")).ConfigureAwait(false);
            await host.Error.WriteLineAsync(TerminalText.Safe(ex.ToString())).ConfigureAwait(false);
            return (int)ExitCode.GeneralFailure;
        }
    }

    private static async Task<int> UsageErrorAsync(CliHost host, IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            await host.Error.WriteLineAsync(TerminalText.Safe(message)).ConfigureAwait(false);
        }

        await host.Error.WriteLineAsync("Run with --help for usage.").ConfigureAwait(false);
        return (int)ExitCode.InvalidUsage;
    }
}
