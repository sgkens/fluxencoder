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

        var stdoutTask = ReadHeadAsync(process.StandardOutput, maxOutputChars);
        var stderrTask = ReadTailAsync(process.StandardError, maxErrorChars);

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

    private static async Task<(string Text, bool Truncated)> ReadHeadAsync(StreamReader reader, int maxChars)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        var truncated = false;
        int read;

        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var room = maxChars - builder.Length;
            if (room > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, room));
            }

            // Keep draining past the limit so the process never blocks on a full pipe.
            truncated |= read > room;
        }

        return (builder.ToString(), truncated);
    }

    private static async Task<string> ReadTailAsync(StreamReader reader, int maxChars)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;

        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > maxChars * 2)
            {
                builder.Remove(0, builder.Length - maxChars);
            }
        }

        if (builder.Length > maxChars)
        {
            builder.Remove(0, builder.Length - maxChars);
        }

        return builder.ToString();
    }
}
