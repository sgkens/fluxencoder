using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace FluxEncoder.Infrastructure.Processes;

/// <summary>
/// Runs a short-lived tool (FFprobe, <c>ffmpeg -encoders</c>) to completion and captures its output.
/// </summary>
/// <remarks>
/// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, never a quoted command string. Stdout and
/// stderr are drained concurrently from start, so a chatty process cannot deadlock on a full pipe. Long-running
/// encodes use the RUN-E004 runner instead, which adds progress parsing and graceful <c>q</c> shutdown.
/// </remarks>
public static class ProcessCapture
{
    public const int DefaultMaxOutputChars = 32 * 1024 * 1024;
    public const int DefaultMaxErrorChars = 64 * 1024;

    /// <exception cref="Win32Exception">The executable could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled; the process is killed first.</exception>
    public static async Task<ProcessOutput> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        int maxOutputChars = DefaultMaxOutputChars,
        int maxErrorChars = DefaultMaxErrorChars)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var startInfo = new ProcessStartInfo(executable)
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
        process.Start();

        // Close stdin so a tool that unexpectedly waits for input sees EOF instead of hanging.
        process.StandardInput.Close();

        var stdoutTask = StreamDrain.ReadHeadAsync(process.StandardOutput, maxOutputChars);
        var stderrTask = StreamDrain.ReadTailAsync(process.StandardError, maxErrorChars);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }

        var (stdout, truncated) = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new ProcessOutput(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut, truncated);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
