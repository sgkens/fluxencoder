using System.CommandLine;
using System.Runtime.InteropServices;
using FluxEncoder.Cli.Commands;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Core.Results;

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
    public static RootCommand BuildRootCommand() => BuildRootCommand(CliHost.Console);

    internal static RootCommand BuildRootCommand(CliHost host)
    {
        var root = new RootCommand("FluxEncoder: a terminal front end for FFmpeg encoding jobs.")
        {
            EncodeCommand.Build(host),
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

        var parse = BuildRootCommand(host).Parse(args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
            {
                await host.Error.WriteLineAsync(error.Message).ConfigureAwait(false);
            }

            await host.Error.WriteLineAsync("Run with --help for usage.").ConfigureAwait(false);
            return (int)ExitCode.InvalidUsage;
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

            await host.Error.WriteLineAsync($"Internal error: {ex.Message}").ConfigureAwait(false);
            await host.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return (int)ExitCode.GeneralFailure;
        }
    }
}
