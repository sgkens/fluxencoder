using System.Collections.Immutable;
using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Which audio or subtitle streams to keep. The set of cases is closed; match on the nested records.
/// </summary>
public abstract record TrackSelection
{
    private protected TrackSelection()
    {
    }

    /// <summary>
    /// Every stream of the kind, in source order. The default for audio and subtitles, so no language or
    /// commentary track is lost unless the user asks.
    /// </summary>
    public sealed record All : TrackSelection;

    public sealed record None : TrackSelection;

    /// <summary>
    /// The stream flagged default. For audio, falls back to the first stream when none is flagged; for
    /// subtitles, keeps nothing in that case, since an arbitrary first subtitle would be shown unasked.
    /// </summary>
    public sealed record Default : TrackSelection;

    /// <summary>
    /// Streams matching any listed language, ordered by list position and then source order. The first kept
    /// audio stream becomes the default, so the list reads as a preference order.
    /// </summary>
    /// <remarks>
    /// Streams without a language tag never match. When no audio stream matches, the mapper keeps the
    /// <see cref="Default"/> audio stream and warns rather than producing a silent file.
    /// </remarks>
    public sealed record Languages : TrackSelection
    {
        public Languages(IEnumerable<LanguageTag> preferred)
        {
            ArgumentNullException.ThrowIfNull(preferred);

            var list = preferred.Distinct().ToImmutableArray();
            if (list.IsEmpty)
            {
                throw new ArgumentException("At least one language is required.", nameof(preferred));
            }

            if (list.Contains(default))
            {
                throw new ArgumentException("Languages cannot contain an empty tag.", nameof(preferred));
            }

            Preferred = list;
        }

        public ImmutableArray<LanguageTag> Preferred { get; }

        public bool Equals(Languages? other) => other is not null && Preferred.SequenceEqual(other.Preferred);

        public override int GetHashCode() => Preferred.Aggregate(0, (hash, tag) => HashCode.Combine(hash, tag));
    }
}
