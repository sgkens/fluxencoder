using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace FluxEncoder.Core.Tooling;

/// <summary>
/// Muxers or demuxers indexed by every alias. FFmpeg's display names are comma-joined alias lists
/// (<c>mov,mp4,m4a,3gp,3g2,mj2</c>), so a lookup for <c>mp4</c> must find the <c>mov</c> entry rather than
/// treat the joined string as a key.
/// </summary>
public sealed class FormatCatalog : IReadOnlyList<FormatEntry>
{
    private readonly ImmutableArray<FormatEntry> _entries;
    private readonly FrozenDictionary<string, FormatEntry> _byAlias;

    public FormatCatalog(IEnumerable<FormatEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = [.. entries];

        // First listing wins for an alias that appears twice, matching FFmpeg's own lookup order.
        var byAlias = new Dictionary<string, FormatEntry>(StringComparer.Ordinal);
        foreach (var entry in _entries)
        {
            foreach (var alias in entry.Names)
            {
                byAlias.TryAdd(alias, entry);
            }
        }

        _byAlias = byAlias.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static FormatCatalog Empty { get; } = new([]);

    public int Count => _entries.Length;

    public FormatEntry this[int index] => _entries[index];

    /// <summary>
    /// Finds the entry for one alias, or for the first resolvable alias of a comma-joined name.
    /// </summary>
    public bool TryFind(string? name, [NotNullWhen(true)] out FormatEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var alias in name.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (_byAlias.TryGetValue(alias, out entry))
            {
                return true;
            }
        }

        return false;
    }

    public bool Contains(string? name) => TryFind(name, out _);

    /// <summary>
    /// Whether two names refer to the same format entry, for example <c>webm</c> and <c>matroska,webm</c>.
    /// </summary>
    public bool AreSameFormat(string? first, string? second) =>
        TryFind(first, out var a) && TryFind(second, out var b) && ReferenceEquals(a, b);

    public IEnumerator<FormatEntry> GetEnumerator() => ((IEnumerable<FormatEntry>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
