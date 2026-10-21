using System.Collections.Immutable;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Infrastructure.Probing;

/// <summary>
/// A validated input ready for planning.
/// </summary>
/// <param name="IsProbed">
/// <see langword="false"/> for pipes, which are never probed; <paramref name="Media"/> then has no streams and
/// the planner must fall back to FFmpeg's own stream selection.
/// </param>
public sealed record ProbedInput(InputSource Source, MediaInfo Media, bool IsProbed, ImmutableArray<JobWarning> Warnings)
{
    public PlanningInput ToPlanningInput() => new(Source, Media, IsProbed);
}
