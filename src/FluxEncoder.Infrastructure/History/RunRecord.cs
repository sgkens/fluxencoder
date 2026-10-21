using System.Collections.Immutable;
using System.Globalization;

namespace FluxEncoder.Infrastructure.History;

/// <summary>
/// One finished run as stored in history.
/// </summary>
public sealed record RunRecord
{
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public required Guid Id { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset FinishedUtc { get; init; }

    public required string Input { get; init; }

    public required string Output { get; init; }

    /// <summary>
    /// <c>succeeded</c>, <c>skipped</c>, or <c>failed</c>.
    /// </summary>
    public required string Outcome { get; init; }

    public required int ExitCode { get; init; }

    public string? FailureCategory { get; init; }

    public string? FailureMessage { get; init; }

    public string? VideoEncoder { get; init; }

    public string? Hardware { get; init; }

    public int Attempts { get; init; } = 1;

    public double ElapsedSeconds { get; init; }

    public long? InputBytes { get; init; }

    public long? OutputBytes { get; init; }

    /// <summary>
    /// The redacted command line; never the raw arguments.
    /// </summary>
    public string? Command { get; init; }

    public ImmutableArray<string> WarningCodes { get; init; } = [];

    public bool Equals(RunRecord? other) =>
        other is not null
        && Id == other.Id && StartedUtc == other.StartedUtc && FinishedUtc == other.FinishedUtc
        && Input == other.Input && Output == other.Output && Outcome == other.Outcome && ExitCode == other.ExitCode
        && FailureCategory == other.FailureCategory && FailureMessage == other.FailureMessage
        && VideoEncoder == other.VideoEncoder && Hardware == other.Hardware && Attempts == other.Attempts
        && ElapsedSeconds.Equals(other.ElapsedSeconds) && InputBytes == other.InputBytes && OutputBytes == other.OutputBytes
        && Command == other.Command && WarningCodes.SequenceEqual(other.WarningCodes);

    public override int GetHashCode() => HashCode.Combine(Id, StartedUtc, Outcome);

    /// <summary>
    /// A new time-ordered identifier (UUID version 7).
    /// </summary>
    public static Guid NewId() => Guid.CreateVersion7();

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseTimestamp(string text) =>
        DateTimeOffset.ParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
