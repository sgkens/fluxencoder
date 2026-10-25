using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Probing;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Execution;

/// <summary>
/// Runs one concat/merge job end to end: probe every input, select a hardware strategy when re-encoding, plan with
/// <see cref="ConcatPlanner"/>, execute, and retry on the CPU when a permitted hardware encode fails at run time
/// (TSKE012.1).
/// </summary>
public static class ConcatOrchestrator
{
    public static async Task<EncodeOutcome> RunAsync(
        ConcatJob job,
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

        var (result, planned) = await PlanAndExecuteAsync(job, prepared.Inputs!, environment, strategy, progress, cancellationToken).ConfigureAwait(false);

        if (result.Failure is { Category: FailureCategory.HardwareUnavailable } hardwareFailure
            && prepared.Selection.FallbackAllowed
            && strategy.Vendor != HardwareVendor.Cpu)
        {
            warnings.AddRange(result.Warnings);
            warnings.Add(new JobWarning(
                WarningCodes.HardwareFallback,
                $"{strategy.DisplayName} failed during the merge ({hardwareFailure.Message}); encoding on the CPU instead."));

            strategy = environment.Registry.Cpu;
            (result, planned) = await PlanAndExecuteAsync(job, prepared.Inputs!, environment, strategy, progress, cancellationToken).ConfigureAwait(false);
        }

        return new EncodeOutcome(result.WithEarlierWarnings(warnings), planned, strategy, prepared.Probes);
    }

    public static async Task<(Result<PlannedJob> Planned, IHardwareStrategy? Strategy, IReadOnlyList<EncoderProbe> Probes, IReadOnlyList<JobWarning> Warnings)> PlanAsync(
        ConcatJob job,
        EncodeEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(job, environment, cancellationToken).ConfigureAwait(false);
        if (prepared.Failure is { } failure)
        {
            return (failure, null, prepared.Probes, prepared.Warnings);
        }

        var strategy = prepared.Selection!.Strategy;
        var planned = ConcatPlanner.Plan(job, [.. prepared.Inputs!.Select(i => i.ToPlanningInput())], environment.PlanningContext(strategy));
        return (planned, strategy, prepared.Probes, prepared.Warnings);
    }

    private sealed record Prepared(
        IReadOnlyList<ProbedInput>? Inputs,
        HardwareSelection? Selection,
        IReadOnlyList<EncoderProbe> Probes,
        IReadOnlyList<JobWarning> Warnings,
        JobFailure? Failure);

    private static async Task<Prepared> PrepareAsync(ConcatJob job, EncodeEnvironment environment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(environment);

        var inputs = new List<ProbedInput>(job.Inputs.Length);
        var warnings = new List<JobWarning>();

        foreach (var path in job.Inputs)
        {
            var probed = await MediaProber.ProbeAsync(environment.FFprobePath, path, cancellationToken, environment.Policy).ConfigureAwait(false);
            if (!probed.IsSuccess)
            {
                return new Prepared(null, null, [], warnings, probed.Failure);
            }

            inputs.Add(probed.Value);
            warnings.AddRange(probed.Value.Warnings);
        }

        var planningInputs = inputs.Select(i => i.ToPlanningInput()).ToList();
        var needsReencode = job.Mode == ConcatMode.Reencode
            || job.Video is VideoTarget.Encode
            || (job.Mode == ConcatMode.Auto && ConcatPlanner.CompatibilityProblems(planningInputs).Count > 0);

        var encodingJob = job.ToEncodingJob();
        if (needsReencode && encodingJob.Video is VideoTarget.StreamCopy)
        {
            encodingJob = encodingJob with { Video = new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Balanced) };
        }

        IReadOnlyList<EncoderProbe> probes = [];
        if (encodingJob.Video is VideoTarget.Encode encode)
        {
            var candidates = HardwareSelector.StrategiesToProbe(encodingJob, inputs[0].Media, environment.Registry, environment.Capabilities.HasEncoder);
            probes = await EncoderProber.ProbeAllAsync(environment.FFmpegPath, candidates, encode.Codec, environment.Capabilities, cancellationToken).ConfigureAwait(false);
        }

        var selection = HardwareSelector.Select(encodingJob, inputs[0].Media, environment.Registry, probes);
        if (!selection.IsSuccess)
        {
            return new Prepared(inputs, null, probes, warnings, selection.Failure);
        }

        warnings.AddRange(selection.Value.Warnings);
        return new Prepared(inputs, selection.Value, probes, warnings, null);
    }

    private static async Task<(JobResult Result, PlannedJob? Planned)> PlanAndExecuteAsync(
        ConcatJob job,
        IReadOnlyList<ProbedInput> inputs,
        EncodeEnvironment environment,
        IHardwareStrategy strategy,
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken)
    {
        var planned = ConcatPlanner.Plan(job, [.. inputs.Select(i => i.ToPlanningInput())], environment.PlanningContext(strategy));
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
