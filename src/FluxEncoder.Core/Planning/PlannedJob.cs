using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// The planner's decision for one job: an executable plan, or a skip required by the overwrite policy.
/// </summary>
public sealed record PlannedJob
{
    private PlannedJob(string outputPath, InputTimeline timeline, ImmutableArray<JobWarning> warnings)
    {
        OutputPath = outputPath;
        Timeline = timeline;
        Warnings = warnings;
    }

    public string OutputPath { get; }

    public OutputPlan? Plan { get; private init; }

    /// <summary>
    /// The timeline progress is measured against: the trimmed length for a trimmed finite input.
    /// </summary>
    public InputTimeline Timeline { get; }

    public ImmutableArray<JobWarning> Warnings { get; }

    public string? SkipReason { get; private init; }

    [MemberNotNullWhen(true, nameof(SkipReason))]
    [MemberNotNullWhen(false, nameof(Plan))]
    public bool IsSkipped => SkipReason is not null;

    public static PlannedJob Run(OutputPlan plan, InputTimeline timeline, IEnumerable<JobWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(plan.FinalOutputPath, timeline, [.. warnings]) { Plan = plan };
    }

    public static PlannedJob Skip(string outputPath, string reason, InputTimeline timeline, IEnumerable<JobWarning> warnings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(outputPath, timeline, [.. warnings]) { SkipReason = reason };
    }

    public bool Equals(PlannedJob? other) =>
        other is not null
        && OutputPath == other.OutputPath
        && Plan == other.Plan
        && Timeline == other.Timeline
        && SkipReason == other.SkipReason
        && Warnings.SequenceEqual(other.Warnings);

    public override int GetHashCode() => HashCode.Combine(OutputPath, Plan, SkipReason);
}
