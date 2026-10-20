using System.Collections.Immutable;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// The resolved, executable form of an <see cref="EncodingJob"/>: the FFmpeg invocations to run in order.
/// A plan is produced without starting a process, which is what makes <c>--dry-run</c> (TSKE007.3) and
/// argument snapshot tests possible.
/// </summary>
/// <remarks>
/// Single-pass jobs have one invocation; two-pass encodes (TSKE005.6) have two.
/// </remarks>
public sealed record OutputPlan
{
    public OutputPlan(EncodingJob job, IEnumerable<FFmpegInvocation> invocations)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(invocations);

        var copy = invocations.ToImmutableArray();
        if (copy.IsEmpty)
        {
            throw new ArgumentException("A plan needs at least one invocation.", nameof(invocations));
        }

        if (copy.Any(i => i is null))
        {
            throw new ArgumentException("Invocations cannot be null.", nameof(invocations));
        }

        Job = job;
        Invocations = copy;
    }

    public EncodingJob Job { get; }

    public ImmutableArray<FFmpegInvocation> Invocations { get; }

    public bool Equals(OutputPlan? other) =>
        other is not null && Job == other.Job && Invocations.SequenceEqual(other.Invocations);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Job);
        foreach (var invocation in Invocations)
        {
            hash.Add(invocation);
        }

        return hash.ToHashCode();
    }
}
