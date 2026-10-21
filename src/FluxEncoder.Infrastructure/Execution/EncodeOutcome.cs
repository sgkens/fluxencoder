using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Infrastructure.Execution;

/// <param name="Result">The job's outcome, carrying warnings from every stage.</param>
/// <param name="Planned">The plan that ran last (the CPU plan after a fallback), or <see langword="null"/> when
/// the job failed before planning.</param>
/// <param name="Strategy">The strategy that produced <paramref name="Planned"/>.</param>
/// <param name="Probes">Hardware encoder probes made for the selection.</param>
public sealed record EncodeOutcome(JobResult Result, PlannedJob? Planned, IHardwareStrategy? Strategy, IReadOnlyList<EncoderProbe> Probes);
