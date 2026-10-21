using System.Diagnostics;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Infrastructure.Execution;

/// <summary>
/// Executes an <see cref="OutputPlan"/>: runs its passes, validates the temporary output, moves it into place,
/// retries transient failures, and always cleans up.
/// </summary>
/// <remarks>
/// <para>
/// Cleanup runs in <c>finally</c> for success, failure, and cancellation: the scratch directory (pass logs) is
/// always deleted, and a temporary output that was not moved into place is deleted unless
/// <see cref="PartialOutputPolicy.Keep"/> applies. Cleanup problems become warnings; they never mask the job's
/// own outcome.
/// </para>
/// <para>
/// The final move never replaces an existing file unless <see cref="OverwritePolicy.Overwrite"/> applies. This
/// closes the race between the planner's existence check and the end of the encode.
/// </para>
/// </remarks>
public static class EncodeExecutor
{
    public static async Task<JobResult> ExecuteAsync(
        string ffmpegPath,
        OutputPlan plan,
        InputTimeline timeline,
        IProgress<JobProgress>? progress = null,
        FFmpegRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(timeline);
        options ??= FFmpegRunOptions.Default;

        var stopwatch = Stopwatch.StartNew();
        var policy = plan.Job.Policy;
        var warnings = new List<JobWarning>();
        var attempt = 0;
        var placed = false;
        JobFailure? failure;

        try
        {
            while (true)
            {
                attempt++;
                DeleteTemporary(plan, warnings);

                if (PrepareDirectories(plan) is { } prepareFailure)
                {
                    failure = prepareFailure;
                    break;
                }

                var outcome = await RunPassesAsync(ffmpegPath, plan, timeline, attempt, progress, options, cancellationToken).ConfigureAwait(false);

                failure = outcome.Failure ?? PlaceOutput(plan, policy);
                if (failure is null)
                {
                    placed = true;
                    if (outcome.StoppedByUser)
                    {
                        warnings.Add(new JobWarning(WarningCodes.StoppedByUser, "Stopped by request; the recording was finalized."));
                    }

                    break;
                }

                if (!failure.IsTransient
                    || !timeline.SupportsJobRetry
                    || !policy.Retry.ShouldRetry(failure, attempt)
                    || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Task.Delay(policy.Retry.GetDelay(attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    failure = new JobFailure(FailureCategory.Cancelled, "Cancelled while waiting to retry.");
                    break;
                }
            }
        }
        finally
        {
            Cleanup(plan, placed, warnings);
        }

        // Built after cleanup so its warnings are included.
        var result = failure is null
            ? JobResult.Succeeded(plan.FinalOutputPath, warnings)
            : JobResult.Failed(plan.FinalOutputPath, failure, warnings);

        return result with { Elapsed = stopwatch.Elapsed, Attempts = attempt };
    }

    private static async Task<PassOutcome> RunPassesAsync(
        string ffmpegPath,
        OutputPlan plan,
        InputTimeline timeline,
        int attempt,
        IProgress<JobProgress>? progress,
        FFmpegRunOptions options,
        CancellationToken cancellationToken)
    {
        var passCount = plan.Invocations.Length;

        for (var index = 0; index < passCount; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new PassOutcome(new JobFailure(FailureCategory.Cancelled, "Cancelled."), StoppedByUser: false);
            }

            var pass = index + 1;
            var passProgress = progress is null
                ? null
                : new SynchronousProgress<EncodeProgress>(report =>
                {
                    var passFraction = timeline.Fraction(report.ProcessedTime ?? TimeSpan.Zero);
                    var overall = passFraction is { } f ? (index + (report.IsFinal ? 1.0 : f)) / passCount : (double?)null;
                    progress.Report(new JobProgress(pass, passCount, report, overall, attempt));
                });

            var result = await FFmpegRunner.RunAsync(ffmpegPath, plan.Invocations[index], passProgress, options, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return new PassOutcome(result.Failure, StoppedByUser: false);
            }

            var run = result.Value;
            if (run.Succeeded)
            {
                continue;
            }

            // Stopping an unbounded single-pass recording is how it normally ends.
            if (run.StoppedCleanly && timeline.StopCompletesJob && passCount == 1)
            {
                return new PassOutcome(null, StoppedByUser: true);
            }

            return new PassOutcome(run.ToFailure(options.StallTimeout), StoppedByUser: false);
        }

        return new PassOutcome(null, StoppedByUser: false);
    }

    /// <summary>
    /// Creates the output directory (the planner allows missing ones) and the scratch directory, and writes the
    /// plan's prepared files.
    /// </summary>
    private static JobFailure? PrepareDirectories(OutputPlan plan)
    {
        var outputDirectory = Path.GetDirectoryName(plan.TemporaryOutputPath ?? plan.FinalOutputPath);

        try
        {
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (plan.ScratchDirectory is { } scratch)
            {
                Directory.CreateDirectory(scratch);
            }

            foreach (var (path, content) in plan.PreparedFiles)
            {
                File.WriteAllText(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new JobFailure(FailureCategory.OutputNotWritable, $"Could not create '{outputDirectory}'.") { Detail = ex.Message };
        }
    }

    private static JobFailure? PlaceOutput(OutputPlan plan, JobPolicy policy)
    {
        if (plan.TemporaryOutputPath is not { } temporary)
        {
            return null;
        }

        var written = new FileInfo(temporary);
        if (!written.Exists || written.Length == 0)
        {
            return new JobFailure(FailureCategory.OutputValidationFailed, "FFmpeg reported success but wrote no output.")
            {
                Detail = $"Expected output at '{temporary}'.",
            };
        }

        try
        {
            File.Move(temporary, plan.FinalOutputPath, overwrite: policy.Overwrite == OverwritePolicy.Overwrite);
            return null;
        }
        catch (IOException) when (File.Exists(plan.FinalOutputPath) && policy.Overwrite != OverwritePolicy.Overwrite)
        {
            return new JobFailure(FailureCategory.OutputExists, $"'{plan.FinalOutputPath}' appeared while encoding and was not replaced.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new JobFailure(FailureCategory.OutputNotWritable, $"Could not move the result to '{plan.FinalOutputPath}'.") { Detail = ex.Message };
        }
    }

    private static void DeleteTemporary(OutputPlan plan, List<JobWarning> warnings)
    {
        if (plan.TemporaryOutputPath is { } temporary)
        {
            TryCleanup(() => File.Delete(temporary), temporary, warnings);
        }
    }

    private static void Cleanup(OutputPlan plan, bool placed, List<JobWarning> warnings)
    {
        if (plan.ScratchDirectory is { } scratch && Directory.Exists(scratch))
        {
            TryCleanup(() => Directory.Delete(scratch, recursive: true), scratch, warnings);
        }

        if (placed || plan.TemporaryOutputPath is not { } temporary || !File.Exists(temporary))
        {
            return;
        }

        if (plan.Job.Policy.PartialOutput == PartialOutputPolicy.Keep)
        {
            warnings.Add(new JobWarning(WarningCodes.PartialOutputKept, $"The partial output was kept at '{temporary}'."));
            return;
        }

        TryCleanup(() => File.Delete(temporary), temporary, warnings);
    }

    private static void TryCleanup(Action action, string path, List<JobWarning> warnings)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(new JobWarning(WarningCodes.CleanupFailed, $"Could not remove '{path}': {ex.Message}"));
        }
    }

    private sealed record PassOutcome(JobFailure? Failure, bool StoppedByUser);

    /// <summary>
    /// Reports synchronously on the calling thread, unlike <see cref="Progress{T}"/>, so pass and attempt
    /// numbers cannot arrive out of order.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
