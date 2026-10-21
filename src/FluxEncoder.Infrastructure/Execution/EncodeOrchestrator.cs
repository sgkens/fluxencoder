using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Probing;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Execution;

/// <summary>
/// Runs one encode end to end: probe the input, probe candidate hardware encoders, select a strategy, plan, execute,
/// and retry on the CPU when a permitted hardware encode fails at run time.
/// </summary>
/// <remarks>
/// Every failure is returned as a <see cref="JobResult"/>; nothing here throws for an expected problem. Warnings from
/// each stage are carried into the final result in order: input, hardware, plan, execution.
/// </remarks>
public static class EncodeOrchestrator
{
    public static async Task<EncodeOutcome> RunAsync(
        EncodingJob job,
        EncodeEnvironment environment,
        IProgress<JobProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(job, environment, cancellationToken).ConfigureAwait(false);
        if (prepared.Failure is { } failure)
        {
            return new EncodeOutcome(JobResult.Failed(job.OutputPath, failure, prepared.Warnings), null, null, prepared.Probes);
        }

        var warnings = new List<JobWarning>(prepared.Warnings);
        var strategy = prepared.Selection!.Strategy;

        var (result, planned) = await PlanAndExecuteAsync(job, prepared.Input!, environment, strategy, progress, cancellationToken).ConfigureAwait(false);

        if (result.Failure is { Category: FailureCategory.HardwareUnavailable } hardwareFailure
            && prepared.Selection.FallbackAllowed
            && strategy.Vendor != HardwareVendor.Cpu)
        {
            warnings.AddRange(result.Warnings);
            warnings.Add(new JobWarning(
                WarningCodes.HardwareFallback,
                $"{strategy.DisplayName} failed during the encode ({hardwareFailure.Message}); encoding on the CPU instead."));

            strategy = environment.Registry.Cpu;
            (result, planned) = await PlanAndExecuteAsync(job, prepared.Input!, environment, strategy, progress, cancellationToken).ConfigureAwait(false);
        }

        return new EncodeOutcome(result.WithEarlierWarnings(warnings), planned, strategy, prepared.Probes);
    }

    /// <summary>
    /// Everything <see cref="RunAsync"/> does before executing: probe, select hardware, and plan. Used by
    /// <c>--dry-run</c>, so the plan shown is exactly the one that would run.
    /// </summary>
    /// <returns>The plan (or the failure that prevented it), with input and hardware warnings merged in front.</returns>
    public static async Task<(Result<PlannedJob> Planned, IHardwareStrategy? Strategy, IReadOnlyList<EncoderProbe> Probes, IReadOnlyList<JobWarning> Warnings)> PlanAsync(
        EncodingJob job,
        EncodeEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(job, environment, cancellationToken).ConfigureAwait(false);
        if (prepared.Failure is { } failure)
        {
            return (failure, null, prepared.Probes, prepared.Warnings);
        }

        var strategy = prepared.Selection!.Strategy;
        var planned = EncodePlanner.Plan(job, prepared.Input!.ToPlanningInput(), environment.PlanningContext(strategy));
        return (planned, strategy, prepared.Probes, prepared.Warnings);
    }

    private sealed record Prepared(ProbedInput? Input, HardwareSelection? Selection, IReadOnlyList<EncoderProbe> Probes, IReadOnlyList<JobWarning> Warnings, JobFailure? Failure);

    private static async Task<Prepared> PrepareAsync(EncodingJob job, EncodeEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(environment);

        var probed = await MediaProber.ProbeAsync(environment.FFprobePath, job.InputPath, cancellationToken).ConfigureAwait(false);
        if (!probed.IsSuccess)
        {
            return new Prepared(null, null, [], [], probed.Failure);
        }

        var input = probed.Value;
        var warnings = new List<JobWarning>(input.Warnings);

        IReadOnlyList<EncoderProbe> probes = [];
        if (job.Video is VideoTarget.Encode encode)
        {
            var candidates = HardwareSelector.StrategiesToProbe(job, input.Media, environment.Registry, environment.Capabilities.HasEncoder);
            probes = await EncoderProber.ProbeAllAsync(environment.FFmpegPath, candidates, encode.Codec, environment.Capabilities, cancellationToken).ConfigureAwait(false);
        }

        var selection = HardwareSelector.Select(job, input.Media, environment.Registry, probes);
        if (!selection.IsSuccess)
        {
            return new Prepared(input, null, probes, warnings, selection.Failure);
        }

        warnings.AddRange(selection.Value.Warnings);
        return new Prepared(input, selection.Value, probes, warnings, null);
    }

    private static async Task<(JobResult Result, PlannedJob? Planned)> PlanAndExecuteAsync(
        EncodingJob job,
        ProbedInput input,
        EncodeEnvironment environment,
        IHardwareStrategy strategy,
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken)
    {
        var planned = EncodePlanner.Plan(job, input.ToPlanningInput(), environment.PlanningContext(strategy));
        if (!planned.IsSuccess)
        {
            return (JobResult.Failed(job.OutputPath, planned.Failure), null);
        }

        var plan = planned.Value;
        if (plan.IsSkipped)
        {
            return (JobResult.Skipped(plan.OutputPath, plan.SkipReason, plan.Warnings), plan);
        }

        var result = await EncodeExecutor.ExecuteAsync(
            environment.FFmpegPath, plan.Plan, plan.Timeline, progress, environment.RunOptions, cancellationToken).ConfigureAwait(false);

        return (result.WithEarlierWarnings(plan.Warnings), plan);
    }
}
