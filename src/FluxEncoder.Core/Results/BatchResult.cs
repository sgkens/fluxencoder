using System.Collections.Immutable;

namespace FluxEncoder.Core.Results;

/// <summary>
/// Results of a batch, in submission order, with the exit code the process should return.
/// </summary>
/// <remarks>
/// A single-file run is a batch of one, so it returns that job's own exit code.
/// </remarks>
public sealed record BatchResult
{
    public BatchResult(IEnumerable<JobResult> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = [.. items];
        if (Items.Any(i => i is null))
        {
            throw new ArgumentException("Batch items cannot be null.", nameof(items));
        }
    }

    public ImmutableArray<JobResult> Items { get; }

    /// <summary>
    /// Items not attempted because the batch stopped early (<c>BatchFailurePolicy.Stop</c> or cancellation).
    /// </summary>
    public int NotStarted
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    public int SucceededCount => Items.Count(i => i.Outcome == JobOutcome.Succeeded);

    public int SkippedCount => Items.Count(i => i.Outcome == JobOutcome.Skipped);

    public int FailedCount => Items.Count(i => i.Outcome == JobOutcome.Failed);

    /// <summary>
    /// Resolves the process exit code, in priority order:
    /// cancellation anywhere → <see cref="ExitCode.Cancelled"/>;
    /// no failures → <see cref="ExitCode.Success"/> (an empty batch included);
    /// some items succeeded or were skipped → <see cref="ExitCode.PartialFailure"/>;
    /// every failure shares one exit code → that code;
    /// otherwise → <see cref="ExitCode.GeneralFailure"/>.
    /// </summary>
    public ExitCode ExitCode
    {
        get
        {
            var failures = Items.Where(i => i.Failure is not null).Select(i => i.Failure!.ExitCode).ToList();

            if (failures.Contains(ExitCode.Cancelled))
            {
                return ExitCode.Cancelled;
            }

            if (failures.Count == 0)
            {
                return ExitCode.Success;
            }

            if (failures.Count < Items.Length)
            {
                return ExitCode.PartialFailure;
            }

            return failures.Distinct().Count() == 1 ? failures[0] : ExitCode.GeneralFailure;
        }
    }

    public bool Equals(BatchResult? other) =>
        other is not null && NotStarted == other.NotStarted && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => HashCode.Combine(Items.Length, NotStarted);
}
