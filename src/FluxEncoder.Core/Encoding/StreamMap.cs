using System.Collections.Immutable;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// The output layout chosen by <see cref="StreamMapper"/>, before container constraints are applied.
/// </summary>
/// <param name="Streams">
/// Output order: motion video, audio, subtitles, cover art, data, attachments.
/// </param>
public sealed record StreamMap(
    ImmutableArray<MappedStream> Streams,
    bool Chapters,
    bool Metadata,
    ImmutableArray<JobWarning> Warnings)
{
    /// <summary>
    /// Nothing was selected. The planner reports this as <c>FailureCategory.InvalidInput</c>.
    /// </summary>
    public bool IsEmpty => Streams.IsEmpty;

    public bool Equals(StreamMap? other) =>
        other is not null
        && Streams.SequenceEqual(other.Streams)
        && Chapters == other.Chapters
        && Metadata == other.Metadata
        && Warnings.SequenceEqual(other.Warnings);

    public override int GetHashCode() => HashCode.Combine(Streams.Length, Chapters, Metadata, Warnings.Length);
}
