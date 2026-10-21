using System.Collections.Frozen;

namespace FluxEncoder.Core.Media;

/// <summary>
/// Builds the read-only metadata dictionaries carried by media models. Keys compare case-insensitively
/// because containers disagree on tag case (<c>title</c>, <c>TITLE</c>).
/// </summary>
public static class MediaTags
{
    public static IReadOnlyDictionary<string, string> Empty { get; } =
        FrozenDictionary<string, string>.Empty.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <remarks>
    /// When keys differ only by case, the first occurrence wins.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Create(IEnumerable<KeyValuePair<string, string>> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in tags)
        {
            result.TryAdd(key, value);
        }

        return result.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
