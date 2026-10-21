using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Processes;

namespace FluxEncoder.Infrastructure.Execution;

/// <summary>
/// Runs one FFmpeg invocation with live progress and deterministic shutdown.
/// </summary>
/// <remarks>
/// <para>
/// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>. The runner prepends
/// <c>-hide_banner -nostats -progress pipe:1 -stats_period 0.5</c>: progress arrives on stdout, diagnostics on
/// stderr, and both are drained concurrently from start and awaited before returning, so neither can deadlock
/// the child or be lost.
/// </para>
/// <para>
/// Stopping (cancellation or a stall) follows one sequence: send <c>q</c> on stdin so FFmpeg finalizes the
/// output, wait <see cref="FFmpegRunOptions.GraceTimeout"/>, kill the process tree if it is still running, then
/// wait for exit. The cancellation callback is a single registration disposed when the run ends. Cancellation
/// is reported in the result, not thrown, because for a live input it is the normal way to finish.
/// </para>
/// <para>
/// Stdin is reserved for the <c>q</c> command, so an invocation that reads its input from <c>pipe:0</c> cannot
/// be stopped gracefully; it is killed after the grace period.
/// </para>
/// </remarks>
public static class FFmpegRunner
{
    private static readonly string[] Preamble = ["-hide_banner", "-nostats", "-progress", "pipe:1", "-stats_period", "0.5"];

    public static async Task<Result<FFmpegRunResult>> RunAsync(
        string ffmpegPath,
        FFmpegInvocation invocation,
        IProgress<EncodeProgress>? progress = null,
        FFmpegRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(invocation);
        options ??= FFmpegRunOptions.Default;

        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in Preamble.Concat(invocation.Arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new JobFailure(FailureCategory.DependencyMissing, $"FFmpeg at '{ffmpegPath}' could not be started.") { Detail = ex.Message };
        }

        var monitor = new ProgressMonitor(progress, new ProcessSampler(process));
        var stdoutTask = ReadProgressAsync(process.StandardOutput, monitor);
        var stderrTask = StreamDrain.ReadTailAsync(process.StandardError, options.MaxErrorChars);

        var stop = new TaskCompletionSource<RunTermination>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watchdogCancellation = new CancellationTokenSource();
        var watchdog = options.StallTimeout is { } stall
            ? WatchForStallAsync(monitor, stall, stop, watchdogCancellation.Token)
            : Task.CompletedTask;

        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        var termination = RunTermination.Exited;
        var killed = false;

        using (cancellationToken.Register(() => stop.TrySetResult(RunTermination.Stopped)))
        {
            if (await Task.WhenAny(exitTask, stop.Task).ConfigureAwait(false) != exitTask)
            {
                termination = await stop.Task.ConfigureAwait(false);
                killed = await StopAsync(process, exitTask, options.GraceTimeout).ConfigureAwait(false);
            }
        }

        await exitTask.ConfigureAwait(false);
        await watchdogCancellation.CancelAsync().ConfigureAwait(false);
        await watchdog.ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        // A stop requested in the same instant FFmpeg finished on its own is still a normal exit.
        if (termination != RunTermination.Exited && !killed && process.ExitCode == 0 && monitor.Last?.IsFinal == true && !cancellationToken.IsCancellationRequested)
        {
            termination = RunTermination.Exited;
        }

        return Result<FFmpegRunResult>.Success(
            new FFmpegRunResult(process.ExitCode, termination, killed, stderr, monitor.Last, stopwatch.Elapsed));
    }

    /// <returns><see langword="true"/> when the process had to be killed.</returns>
    private static async Task<bool> StopAsync(Process process, Task exitTask, TimeSpan grace)
    {
        try
        {
            await process.StandardInput.WriteAsync('q').ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // FFmpeg already closed stdin because it is exiting.
        }

        if (await Task.WhenAny(exitTask, Task.Delay(grace)).ConfigureAwait(false) == exitTask)
        {
            return false;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return true;
    }

    private static async Task ReadProgressAsync(StreamReader reader, ProgressMonitor monitor)
    {
        var parser = new FFmpegProgressParser();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (parser.Accept(line) is { } report)
            {
                monitor.Report(report);
            }
        }
    }

    private static async Task WatchForStallAsync(ProgressMonitor monitor, TimeSpan stall, TaskCompletionSource<RunTermination> stop, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(stall.TotalMilliseconds / 4, 50, 1000));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                if (monitor.SinceLastAdvance > stall)
                {
                    stop.TrySetResult(RunTermination.Stalled);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended first.
        }
    }

    /// <summary>
    /// Tracks the latest report and when output last advanced. A report that repeats the previous frame count,
    /// time, and size is not an advance, which is what makes a hung input detectable.
    /// </summary>
    private sealed class ProgressMonitor(IProgress<EncodeProgress>? sink, ProcessSampler sampler)
    {
        private readonly Stopwatch _sinceAdvance = Stopwatch.StartNew();
        private EncodeProgress? _last;

        public EncodeProgress? Last => Volatile.Read(ref _last);

        public TimeSpan SinceLastAdvance => _sinceAdvance.Elapsed;

        public void Report(EncodeProgress report)
        {
            var previous = Volatile.Read(ref _last);
            if (previous is null
                || report.Frame != previous.Frame
                || report.ProcessedTime != previous.ProcessedTime
                || report.TotalSizeBytes != previous.TotalSizeBytes)
            {
                _sinceAdvance.Restart();
            }

            var (cpu, memory) = sampler.Sample();
            report = report with { CpuPercent = cpu, MemoryBytes = memory };

            Volatile.Write(ref _last, report);
            sink?.Report(report);
        }
    }

    /// <summary>
    /// Measures FFmpeg's CPU share and memory at each progress report (TSKE009.8). CPU is the process's processor-time
    /// delta divided by the wall-clock delta and the logical core count, so 100 means every core was busy.
    /// </summary>
    private sealed class ProcessSampler(Process process)
    {
        private readonly Stopwatch _wall = Stopwatch.StartNew();
        private TimeSpan _lastCpu;
        private TimeSpan _lastWall;

        public (double? CpuPercent, long? MemoryBytes) Sample()
        {
            try
            {
                process.Refresh();
                var cpu = process.TotalProcessorTime;
                var wall = _wall.Elapsed;
                var wallDelta = (wall - _lastWall).TotalSeconds;

                double? percent = wallDelta > 0.05 && _lastWall > TimeSpan.Zero
                    ? Math.Clamp((cpu - _lastCpu).TotalSeconds / wallDelta / Environment.ProcessorCount * 100, 0, 100)
                    : null;

                _lastCpu = cpu;
                _lastWall = wall;
                return (percent, process.WorkingSet64);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // The process exited between reports, or the platform does not expose the counters.
                return (null, null);
            }
        }
    }
}
