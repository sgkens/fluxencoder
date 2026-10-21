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
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(environment);

        var probed = await MediaProber.ProbeAsync(environment.FFprobePath, job.InputPath, cancellationToken).ConfigureAwait(false);
        if (!probed.IsSuccess)
        {
            return new EncodeOutcome(JobResult.Failed(job.OutputPath, probed.Failure), null, null, []);
        }

        var input = probed.Value;
        var warnings = new List<JobWarning>(input.Warnings);
        var registry = environment.Registry;

        IReadOnlyList<EncoderProbe> probes = [];
        if (job.Video is VideoTarget.Encode encode)
        {
            var candidates = HardwareSelector.StrategiesToProbe(job, input.Media, registry, environment.Capabilities.HasEncoder);
            probes = await EncoderProber.ProbeAllAsync(environment.FFmpegPath, candidates, encode.Codec, environment.Capabilities, cancellationToken).ConfigureAwait(false);
        }

        var selection = HardwareSelector.Select(job, input.Media, registry, probes);
        if (!selection.IsSuccess)
        {
            return new EncodeOutcome(JobResult.Failed(job.OutputPath, selection.Failure, warnings), null, null, probes);
        }

        warnings.AddRange(selection.Value.Warnings);
        var strategy = selection.Value.Strategy;

        var (result, planned) = await PlanAndExecuteAsync(job, input, environment, strategy, progress, cancellationToken).ConfigureAwait(false);

        if (result.Failure is { Category: FailureCategory.HardwareUnavailable } hardwareFailure
            && selection.Value.FallbackAllowed
            && strategy.Vendor != HardwareVendor.Cpu)
        {
            warnings.AddRange(result.Warnings);
            warnings.Add(new JobWarning(
                WarningCodes.HardwareFallback,
                $"{strategy.DisplayName} failed during the encode ({hardwareFailure.Message}); encoding on the CPU instead."));

            strategy = registry.Cpu;
            (result, planned) = await PlanAndExecuteAsync(job, input, environment, strategy, progress, cancellationToken).ConfigureAwait(false);
        }

        return new EncodeOutcome(result.WithEarlierWarnings(warnings), planned, strategy, probes);
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
