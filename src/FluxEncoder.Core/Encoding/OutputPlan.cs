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

    /// <summary>
    /// Where the result ends up. Equals <see cref="EncodingJob.OutputPath"/> unless
    /// <see cref="OverwritePolicy.Rename"/> chose a numbered sibling.
    /// </summary>
    public string FinalOutputPath
    {
        get => field ?? Job.OutputPath;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>
    /// The file FFmpeg writes (<see cref="OutputPaths.Temporary"/>), moved to <see cref="FinalOutputPath"/> only
    /// after it validates. <see langword="null"/> when FFmpeg writes straight to the destination, as for network
    /// streams and pipes, where there is nothing to move.
    /// </summary>
    public string? TemporaryOutputPath { get; init; }

    /// <summary>
    /// A directory for pass logs and other per-job scratch files, deleted when the job ends whatever its outcome.
    /// </summary>
    public string? ScratchDirectory { get; init; }

    /// <summary>
    /// Files the executor writes before the first invocation, such as a concat list, keyed by full path. Paths lie
    /// inside <see cref="ScratchDirectory"/> so cleanup removes them.
    /// </summary>
    public ImmutableDictionary<string, string> PreparedFiles { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// The output size limit passed to FFmpeg as <c>-fs</c>, if any. An output that reaches it was cut short, so the
    /// executor fails it instead of promoting it.
    /// </summary>
    public long? MaxOutputBytes { get; init; }

    public bool Equals(OutputPlan? other) =>
        other is not null
        && Job == other.Job
        && Invocations.SequenceEqual(other.Invocations)
        && FinalOutputPath == other.FinalOutputPath
        && TemporaryOutputPath == other.TemporaryOutputPath
        && ScratchDirectory == other.ScratchDirectory
        && MaxOutputBytes == other.MaxOutputBytes
        && PreparedFiles.Count == other.PreparedFiles.Count
        && PreparedFiles.All(file => other.PreparedFiles.TryGetValue(file.Key, out var content) && content == file.Value);

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
