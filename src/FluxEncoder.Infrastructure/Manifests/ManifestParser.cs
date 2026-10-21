using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using FluxEncoder.Core.Manifests;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace FluxEncoder.Infrastructure.Manifests;

/// <summary>
/// Parses YAML or JSON into a <see cref="ManifestNode"/> tree (TSKE011.1), refusing every feature a manifest does not
/// need and an attacker could use.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>YAML anchors and aliases are refused: expanding them is how a few hundred bytes become gigabytes ("billion
/// laughs"), and a job list has no use for them.</item>
/// <item>Explicit YAML tags (<c>!!python/object</c>, <c>!custom</c>) are refused, so nothing in the file can ask for a
/// type to be constructed. Parsing uses YamlDotNet's event parser only: no reflection, no object mapping, Native AOT
/// safe.</item>
/// <item>Duplicate keys are errors in both formats; JSON readers otherwise silently keep the last one.</item>
/// <item>Nesting deeper than <see cref="MaxDepth"/> and more than one YAML document are refused.</item>
/// </list>
/// Scalars stay text; see <see cref="ManifestNode"/>.
/// </remarks>
public static class ManifestParser
{
    public const int MaxDepth = 32;

    private static readonly string[] YamlNulls = ["", "~", "null", "Null", "NULL"];

    public static (ManifestNode? Root, ManifestError? Error) ParseYaml(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            var parser = new Parser(new StringReader(text));
            parser.Consume<StreamStart>();
            if (parser.Accept<StreamEnd>(out _))
            {
                return (null, new ManifestError(null, "The file is empty."));
            }

            parser.Consume<DocumentStart>();
            var root = ReadYaml(parser, 0);
            parser.Consume<DocumentEnd>();
            if (!parser.Accept<StreamEnd>(out _))
            {
                return (null, new ManifestError(Position(parser.Current!.Start), "Only one YAML document is allowed; remove the '---' separators."));
            }

            return (root, null);
        }
        catch (ManifestFormatException ex)
        {
            return (null, new ManifestError(ex.Position, ex.Message));
        }
        catch (YamlException ex)
        {
            return (null, new ManifestError(Position(ex.Start), $"Not valid YAML: {ex.Message}"));
        }
    }

    public static (ManifestNode? Root, ManifestError? Error) ParseJson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, new ManifestError(null, "The file is empty."));
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var lines = new LineMap(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = MaxDepth + 1,
        });

        try
        {
            reader.Read();
            var root = ReadJson(ref reader, lines);

            // Content after the document makes the reader throw, which is reported below.
            reader.Read();
            return (root, null);
        }
        catch (ManifestFormatException ex)
        {
            return (null, new ManifestError(ex.Position, ex.Message));
        }
        catch (JsonException ex)
        {
            ManifestPosition? position = ex.LineNumber is { } line ? new((int)line + 1, (int)(ex.BytePositionInLine ?? 0) + 1) : null;
            return (null, new ManifestError(position, $"Not valid JSON: {FirstSentence(ex.Message)}"));
        }
    }

    private static ManifestNode ReadYaml(IParser parser, int depth)
    {
        var current = parser.Current!;
        var position = Position(current.Start);

        if (current is AnchorAlias)
        {
            throw new ManifestFormatException(position, "YAML aliases (*name) are not allowed in a Fluxfile; write the value out.");
        }

        if (current is NodeEvent node)
        {
            if (!node.Anchor.IsEmpty)
            {
                throw new ManifestFormatException(position, "YAML anchors (&name) are not allowed in a Fluxfile; use 'defaults' for shared options.");
            }

            if (!node.Tag.IsEmpty)
            {
                throw new ManifestFormatException(position, $"YAML tags ({node.Tag}) are not allowed in a Fluxfile.");
            }
        }

        if (depth > MaxDepth)
        {
            throw new ManifestFormatException(position, $"The document is nested more than {MaxDepth} levels deep.");
        }

        switch (current)
        {
            case Scalar scalar:
                parser.MoveNext();
                return new ManifestScalar(
                    scalar.Value,
                    position,
                    IsNull: scalar.Style == ScalarStyle.Plain && YamlNulls.Contains(scalar.Value, StringComparer.Ordinal));

            case SequenceStart:
            {
                parser.MoveNext();
                var items = ImmutableArray.CreateBuilder<ManifestNode>();
                while (!parser.TryConsume<SequenceEnd>(out _))
                {
                    items.Add(ReadYaml(parser, depth + 1));
                }

                return new ManifestSequence(items.ToImmutable(), position);
            }

            case MappingStart:
            {
                parser.MoveNext();
                var entries = ImmutableArray.CreateBuilder<KeyValuePair<ManifestScalar, ManifestNode>>();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                while (!parser.TryConsume<MappingEnd>(out _))
                {
                    if (ReadYaml(parser, depth + 1) is not ManifestScalar key || key.IsNull)
                    {
                        throw new ManifestFormatException(Position(parser.Current!.Start), "Mapping keys must be plain text.");
                    }

                    if (!keys.Add(key.Value))
                    {
                        throw new ManifestFormatException(key.Position, $"Duplicate key '{key.Value}'.");
                    }

                    entries.Add(new(key, ReadYaml(parser, depth + 1)));
                }

                return new ManifestMapping(entries.ToImmutable(), position);
            }

            default:
                throw new ManifestFormatException(position, $"Unexpected YAML content ({current.GetType().Name}).");
        }
    }

    private static ManifestNode ReadJson(ref Utf8JsonReader reader, LineMap lines)
    {
        var position = lines.Position(reader.TokenStartIndex);
        if (reader.CurrentDepth > MaxDepth)
        {
            throw new ManifestFormatException(position, $"The document is nested more than {MaxDepth} levels deep.");
        }

        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new ManifestScalar(reader.GetString()!, position);
            case JsonTokenType.Number:
                return new ManifestScalar(Encoding.UTF8.GetString(reader.ValueSpan), position);
            case JsonTokenType.True or JsonTokenType.False:
                return new ManifestScalar(reader.TokenType == JsonTokenType.True ? "true" : "false", position);
            case JsonTokenType.Null:
                return new ManifestScalar(string.Empty, position, IsNull: true);

            case JsonTokenType.StartArray:
            {
                var items = ImmutableArray.CreateBuilder<ManifestNode>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    items.Add(ReadJson(ref reader, lines));
                }

                return new ManifestSequence(items.ToImmutable(), position);
            }

            case JsonTokenType.StartObject:
            {
                var entries = ImmutableArray.CreateBuilder<KeyValuePair<ManifestScalar, ManifestNode>>();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var key = new ManifestScalar(reader.GetString()!, lines.Position(reader.TokenStartIndex));
                    if (!keys.Add(key.Value))
                    {
                        throw new ManifestFormatException(key.Position, $"Duplicate key '{key.Value}'.");
                    }

                    reader.Read();
                    entries.Add(new(key, ReadJson(ref reader, lines)));
                }

                return new ManifestMapping(entries.ToImmutable(), position);
            }

            default:
                throw new ManifestFormatException(position, $"Unexpected JSON token {reader.TokenType}.");
        }
    }

    private static ManifestPosition Position(Mark mark) => new((int)mark.Line, (int)mark.Column);

    private static string FirstSentence(string message) =>
        message.IndexOf(" LineNumber:", StringComparison.Ordinal) is var cut and > 0 ? message[..cut].TrimEnd() : message;

    /// <summary>
    /// Byte offsets to line and column (in characters), for JSON token positions.
    /// </summary>
    private sealed class LineMap(byte[] bytes)
    {
        private readonly List<int> _starts = Starts(bytes);

        public ManifestPosition Position(long offset)
        {
            var index = _starts.BinarySearch((int)offset);
            var line = index >= 0 ? index : ~index - 1;
            var column = Encoding.UTF8.GetCharCount(bytes, _starts[line], (int)offset - _starts[line]) + 1;
            return new ManifestPosition(line + 1, column);
        }

        private static List<int> Starts(byte[] bytes)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\n')
                {
                    starts.Add(i + 1);
                }
            }

            return starts;
        }
    }

    private sealed class ManifestFormatException(ManifestPosition? position, string message) : Exception(message)
    {
        public ManifestPosition? Position { get; } = position;
    }
}
