using System.Collections.Immutable;

namespace FluxEncoder.Infrastructure.Configuration;

/// <summary>
/// A named set of <c>encode</c> option values.
/// </summary>
public sealed record SettingsPreset(string? Description, ImmutableDictionary<string, string> Options)
{
    public bool Equals(SettingsPreset? other) =>
        other is not null
        && Description == other.Description
        && Options.Count == other.Options.Count
        && Options.All(o => other.Options.TryGetValue(o.Key, out var v) && v == o.Value);

    public override int GetHashCode() => HashCode.Combine(Description, Options.Count);
}
