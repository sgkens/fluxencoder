using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FluxEncoder.Infrastructure.Storage;

namespace FluxEncoder.Infrastructure.Configuration;

/// <summary>
/// Reads and writes <c>settings.json</c> (TSKE009.6).
/// </summary>
/// <remarks>
/// <para>
/// Reading is tolerant and never destructive: a missing file means defaults; a file that is not valid JSON, or a
/// value of the wrong type, produces a warning naming the problem and the fallback, and the user's file is left
/// untouched for them to fix. Unknown keys are warned about rather than silently ignored.
/// </para>
/// <para>
/// Writing is atomic: the new content goes to a temporary file in the same directory, is flushed to disk, and then
/// replaces the old file in one rename, keeping the previous version as <c>settings.json.bak</c>. A crash at any point
/// leaves either the old or the new file, never a truncated one. On Unix the file is created owner-only (0600).
/// </para>
/// </remarks>
public static class SettingsStore
{
    private static readonly string[] TopLevelKeys = ["schemaVersion", "encode", "presets", "history"];

    public static (FluxSettings Settings, IReadOnlyList<string> Warnings) Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return (FluxSettings.Default, []);
        }

        var name = Path.GetFileName(path);
        var warnings = new List<string>();
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (FluxSettings.Default, [$"{name} could not be read ({ex.Message}); using built-in defaults."]);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" at line {line + 1}, position {(ex.BytePositionInLine ?? 0) + 1}" : string.Empty;
            return (FluxSettings.Default, [$"{name} is not valid JSON{where}; using built-in defaults. Fix the file, or replace it with 'config init --force' (the old file is kept as .bak)."]);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (FluxSettings.Default, [$"{name} must contain a JSON object; using built-in defaults."]);
            }

            foreach (var property in root.EnumerateObject().Where(p => !TopLevelKeys.Contains(p.Name, StringComparer.Ordinal)))
            {
                warnings.Add($"{name}: unknown setting '{property.Name}' ignored.");
            }

            if (root.TryGetProperty("schemaVersion", out var version) && version.TryGetInt32(out var schema) && schema > FluxSettings.SchemaVersion)
            {
                warnings.Add($"{name} was written by a newer FluxEncoder (schema {schema}); settings this version does not know are ignored.");
            }

            var settings = new FluxSettings
            {
                Encode = root.TryGetProperty("encode", out var encode) ? ReadOptions(encode, $"{name}: encode", warnings) : ImmutableDictionary<string, string>.Empty,
                Presets = root.TryGetProperty("presets", out var presets) ? ReadPresets(presets, name, warnings) : FluxSettings.Default.Presets,
                History = root.TryGetProperty("history", out var history) ? ReadHistory(history, name, warnings) : HistorySettings.Default,
            };

            return (settings, warnings);
        }
    }

    public static void Save(string path, FluxSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        AppPaths.EnsureDirectory(directory);

        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temporary, options))
            {
                stream.Write(Serialize(settings));
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static byte[] Serialize(FluxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", FluxSettings.SchemaVersion);

            writer.WriteStartObject("encode");
            WriteOptions(writer, settings.Encode);
            writer.WriteEndObject();

            writer.WriteStartObject("presets");
            foreach (var (name, preset) in settings.Presets.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteStartObject(name);
                if (preset.Description is { } description)
                {
                    writer.WriteString("description", description);
                }

                WriteOptions(writer, preset.Options);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();

            writer.WriteStartObject("history");
            writer.WriteBoolean("enabled", settings.History.Enabled);
            writer.WriteBoolean("telemetry", settings.History.Telemetry);
            writer.WriteNumber("retentionDays", settings.History.RetentionDays);
            writer.WriteNumber("maxRuns", settings.History.MaxRuns);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>
    /// The file <c>config init</c> writes: every default spelled out, plus example presets to adapt.
    /// </summary>
    public static FluxSettings Template { get; } = new()
    {
        Encode = ImmutableDictionary<string, string>.Empty
            .Add("codec", "h264")
            .Add("quality", "balanced")
            .Add("audio-codec", "copy")
            .Add("hardware", "auto"),
        Presets = FluxSettings.Default.Presets
            .Add("archive", new SettingsPreset(
                "Small, high-quality HEVC with Opus audio, keeping every track.",
                ImmutableDictionary<string, string>.Empty.Add("codec", "hevc").Add("quality", "high").Add("audio-codec", "opus").Add("format", "mkv")))
            .Add("web", new SettingsPreset(
                "Widely playable MP4 for browsers and phones: H.264, AAC, default audio only, no subtitles.",
                ImmutableDictionary<string, string>.Empty.Add("codec", "h264").Add("audio-codec", "aac").Add("format", "mp4").Add("audio", "default").Add("subs", "none"))),
    };

    private static void WriteOptions(Utf8JsonWriter writer, ImmutableDictionary<string, string> options)
    {
        foreach (var (key, value) in options.OrderBy(o => o.Key, StringComparer.Ordinal))
        {
            if (value is "true" or "false")
            {
                writer.WriteBoolean(key, value == "true");
            }
            else
            {
                writer.WriteString(key, value);
            }
        }
    }

    private static ImmutableDictionary<string, string> ReadOptions(JsonElement element, string context, List<string> warnings, params string[] reserved)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"{context} must be an object of option names and values; ignored.");
            return builder.ToImmutable();
        }

        foreach (var property in element.EnumerateObject())
        {
            if (reserved.Contains(property.Name, StringComparer.Ordinal))
            {
                continue;
            }

            var key = property.Name.TrimStart('-');
            string? value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };

            if (value is null)
            {
                warnings.Add($"{context}.{key}: value must be a string, number, or true/false; ignored.");
                continue;
            }

            builder[key] = value;
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, SettingsPreset> ReadPresets(JsonElement element, string file, List<string> warnings)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, SettingsPreset>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"{file}: presets must be an object of named presets; ignored.");
            return builder.ToImmutable();
        }

        foreach (var preset in element.EnumerateObject())
        {
            if (preset.Value.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"{file}: preset '{preset.Name}' must be an object; ignored.");
                continue;
            }

            var description = preset.Value.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            builder[preset.Name] = new SettingsPreset(description, ReadOptions(preset.Value, $"{file}: presets.{preset.Name}", warnings, "description"));
        }

        return builder.ToImmutable();
    }

    private static HistorySettings ReadHistory(JsonElement element, string file, List<string> warnings)
    {
        var defaults = HistorySettings.Default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"{file}: history must be an object; using defaults.");
            return defaults;
        }

        bool Bool(string key, bool fallback)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                return fallback;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            warnings.Add($"{file}: history.{key} must be true or false; using {fallback.ToString().ToLowerInvariant()}.");
            return fallback;
        }

        int Count(string key, int fallback, int min, int max)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                return fallback;
            }

            if (value.TryGetInt32(out var number) && number >= min && number <= max)
            {
                return number;
            }

            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{file}: history.{key} must be a whole number from {min} to {max}; using {fallback}."));
            return fallback;
        }

        foreach (var property in element.EnumerateObject().Where(p => p.Name is not ("enabled" or "telemetry" or "retentionDays" or "maxRuns")))
        {
            warnings.Add($"{file}: unknown setting 'history.{property.Name}' ignored.");
        }

        return new HistorySettings(
            Bool("enabled", defaults.Enabled),
            Bool("telemetry", defaults.Telemetry),
            Count("retentionDays", defaults.RetentionDays, 1, 3650),
            Count("maxRuns", defaults.MaxRuns, 1, 1_000_000));
    }

    /// <summary>
    /// The settings as a JSON string, for <c>config show</c>.
    /// </summary>
    public static string ToJson(FluxSettings settings) => Encoding.UTF8.GetString(Serialize(settings));
}
