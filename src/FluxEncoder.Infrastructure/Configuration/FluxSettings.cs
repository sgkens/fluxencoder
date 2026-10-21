using System.Collections.Immutable;
using FluxEncoder.Core.Security;

namespace FluxEncoder.Infrastructure.Configuration;

/// <summary>
/// The contents of <c>settings.json</c>.
/// </summary>
/// <remarks>
/// <see cref="Encode"/> and each preset map <c>encode</c> option names (without dashes) to values written exactly as on
/// the command line: <c>{"codec": "hevc", "audio": "jpn,eng", "skip-existing": true}</c>. The CLI validates them with
/// the same parsers as typed flags, so settings can never express something a flag cannot.
/// </remarks>
public sealed record FluxSettings
{
    public const int SchemaVersion = 1;

    public static FluxSettings Default { get; } = new();

    /// <summary>
    /// Defaults for <c>encode</c> options the user does not give.
    /// </summary>
    public ImmutableDictionary<string, string> Encode { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Named option sets selected with <c>--preset NAME</c>; a preset wins over <see cref="Encode"/>, and typed flags win
    /// over both.
    /// </summary>
    public ImmutableDictionary<string, SettingsPreset> Presets { get; init; } =
        ImmutableDictionary<string, SettingsPreset>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public HistorySettings History { get; init; } = HistorySettings.Default;

    /// <summary>
    /// Allowed inputs, protocols, folders, and raw FFmpeg arguments (TSKE010.1, TSKE010.2).
    /// </summary>
    public SourcePolicy Security { get; init; } = SourcePolicy.Default;

    /// <summary>
    /// Ceilings for service use (TSKE010.6).
    /// </summary>
    public ResourceLimits Limits { get; init; } = ResourceLimits.None;

    /// <summary>
    /// Named remote locations for <c>remote://ALIAS/path</c>. No passwords: those live in the credential store.
    /// </summary>
    public ImmutableDictionary<string, RemoteMount> Remotes { get; init; } =
        ImmutableDictionary<string, RemoteMount>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public bool Equals(FluxSettings? other) =>
        other is not null
        && Encode.Count == other.Encode.Count && Encode.All(e => other.Encode.TryGetValue(e.Key, out var v) && v == e.Value)
        && Presets.Count == other.Presets.Count && Presets.All(p => other.Presets.TryGetValue(p.Key, out var v) && v == p.Value)
        && History == other.History
        && Security == other.Security
        && Limits == other.Limits
        && Remotes.Count == other.Remotes.Count && Remotes.All(r => other.Remotes.TryGetValue(r.Key, out var v) && v == r.Value);

    public override int GetHashCode() => HashCode.Combine(Encode.Count, Presets.Count, History);
}
