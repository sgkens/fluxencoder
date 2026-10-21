using System.Text.Json.Serialization;

namespace FluxEncoder.Infrastructure.History;

/// <summary>
/// Source-generated JSON for the small JSON columns, so history stays Native AOT safe.
/// </summary>
[JsonSerializable(typeof(string[]))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext;
