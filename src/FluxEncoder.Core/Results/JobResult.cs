using System.Collections.Immutable;

namespace FluxEncoder.Core.Results;

/// <summary>
/// The outcome of one job. Create instances through <see cref="Succeeded"/>, <see cref="Skipped"/>, and
/// <see cref="Failed"/>, which keep <see cref="Failure"/> and <see cref="SkipReason"/> consistent with
/// <see cref="Outcome"/>.
/// </summary>
public sealed record JobResult
{
    private JobResult(string outputPath, JobOutcome outcome, IEnumerable<JobWarning> warnings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(warnings);

        OutputPath = outputPath;
        Outcome = outcome;
        Warnings = [.. warnings];
    }

    /// <summary>
    /// The destination this job wrote, or would have written. For a failed job the file does not exist
    /// unless the partial-output policy kept it.
    /// </summary>
    public string OutputPath { get; }

    public JobOutcome Outcome { get; }

    public JobFailure? Failure { get; private init; }

    public string? SkipReason { get; private init; }

    public ImmutableArray<JobWarning> Warnings { get; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Attempts made, including the first. Greater than one only when a retry policy re-ran the job.
    /// </summary>
    public int Attempts
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1;

    public ExitCode ExitCode => Failure?.ExitCode ?? ExitCode.Success;

    public static JobResult Succeeded(string outputPath, IEnumerable<JobWarning>? warnings = null) =>
        new(outputPath, JobOutcome.Succeeded, warnings ?? []);

    public static JobResult Skipped(string outputPath, string reason, IEnumerable<JobWarning>? warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(outputPath, JobOutcome.Skipped, warnings ?? []) { SkipReason = reason };
    }

    public static JobResult Failed(string outputPath, JobFailure failure, IEnumerable<JobWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(outputPath, JobOutcome.Failed, warnings ?? []) { Failure = failure };
    }

    public bool Equals(JobResult? other) =>
        other is not null
        && OutputPath == other.OutputPath
        && Outcome == other.Outcome
        && Failure == other.Failure
        && SkipReason == other.SkipReason
        && Warnings.SequenceEqual(other.Warnings)
        && Elapsed == other.Elapsed
        && Attempts == other.Attempts;

    public override int GetHashCode() => HashCode.Combine(OutputPath, Outcome, Failure, SkipReason, Warnings.Length, Elapsed, Attempts);
}
