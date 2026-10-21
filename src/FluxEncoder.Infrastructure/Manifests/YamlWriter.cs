using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace FluxEncoder.Infrastructure.Manifests;

/// <summary>
/// Writes a JSON tree as YAML through YamlDotNet's emitter (TSKE011.5), replacing hand-built YAML.
/// </summary>
/// <remarks>
/// Every string value is double-quoted, so text from media files can never be read back as another type, a
/// structure, an anchor, or a tag: <c>no</c>, <c>1:30</c>, <c>*alias</c>, <c>!!tag</c>, and <c>key: value</c> all stay
/// strings. The emitter escapes control characters and line breaks inside the quotes. Keys are left plain only when
/// they are simple identifiers that YAML cannot read as anything else; any other key is quoted too.
/// </remarks>
public static class YamlWriter
{
    private static readonly HashSet<string> Ambiguous = new(StringComparer.OrdinalIgnoreCase)
    {
        "y", "n", "yes", "no", "on", "off", "true", "false", "null",
    };

    public static string Write(JsonNode? root)
    {
        using var writer = new StringWriter();
        var emitter = new Emitter(writer, new EmitterSettings().WithBestWidth(int.MaxValue));
        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        Emit(emitter, root);
        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());
        return writer.ToString();
    }

    private static void Emit(IEmitter emitter, JsonNode? node)
    {
        switch (node)
        {
            case null:
                emitter.Emit(Plain("null"));
                break;

            case JsonObject obj:
                emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, true, obj.Count == 0 ? MappingStyle.Flow : MappingStyle.Block));
                foreach (var (key, value) in obj)
                {
                    emitter.Emit(IsPlainKey(key) ? Plain(key) : Quoted(key));
                    Emit(emitter, value);
                }

                emitter.Emit(new MappingEnd());
                break;

            case JsonArray array:
                emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, array.Count == 0 ? SequenceStyle.Flow : SequenceStyle.Block));
                foreach (var item in array)
                {
                    Emit(emitter, item);
                }

                emitter.Emit(new SequenceEnd());
                break;

            case JsonValue value:
                emitter.Emit(value.GetValueKind() switch
                {
                    JsonValueKind.String => Quoted(value.GetValue<string>()),
                    JsonValueKind.True => Plain("true"),
                    JsonValueKind.False => Plain("false"),
                    JsonValueKind.Number => Plain(value.ToJsonString()),
                    _ => Plain("null"),
                });
                break;
        }
    }

    private static Scalar Plain(string text) => new(AnchorName.Empty, TagName.Empty, text, ScalarStyle.Plain, true, false);

    private static Scalar Quoted(string text) => new(AnchorName.Empty, TagName.Empty, text, ScalarStyle.DoubleQuoted, false, true);

    private static bool IsPlainKey(string key) =>
        key.Length > 0
        && char.IsAsciiLetter(key[0])
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
        && !Ambiguous.Contains(key);
}
