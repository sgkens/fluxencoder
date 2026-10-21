using System.Collections.Immutable;

namespace FluxEncoder.Core.Manifests;

/// <summary>
/// A line and column in a manifest file, both starting at 1, so errors can point at the exact place.
/// </summary>
public readonly record struct ManifestPosition(int Line, int Column)
{
    public override string ToString() => $"{Line}:{Column}";
}

/// <summary>
/// A manifest as plain data: mappings, sequences, and text. YAML and JSON both parse to this tree, so one reader
/// validates both formats the same way.
/// </summary>
/// <remarks>
/// The tree deliberately has no types beyond text. YAML's implicit typing (where <c>no</c> can mean false and
/// <c>1:30</c> a number) never applies; each field decides how to read its own text.
/// </remarks>
public abstract record ManifestNode(ManifestPosition Position);

/// <param name="IsNull">An explicit null (<c>null</c> or <c>~</c> in YAML, <c>null</c> in JSON) or an empty value.</param>
public sealed record ManifestScalar(string Value, ManifestPosition Position, bool IsNull = false) : ManifestNode(Position);

/// <summary>
/// Keys in file order. Parsers reject duplicate keys, so each key appears once.
/// </summary>
public sealed record ManifestMapping(ImmutableArray<KeyValuePair<ManifestScalar, ManifestNode>> Entries, ManifestPosition Position) : ManifestNode(Position);

public sealed record ManifestSequence(ImmutableArray<ManifestNode> Items, ManifestPosition Position) : ManifestNode(Position);

/// <summary>
/// One problem in a manifest, with its position when it has one.
/// </summary>
public sealed record ManifestError(ManifestPosition? Position, string Message)
{
    public string Format(string file) => Position is { } position ? $"{file}:{position}: {Message}" : $"{file}: {Message}";
}
