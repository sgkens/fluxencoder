using System.Collections.Frozen;
using System.Collections.Immutable;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Tooling;

namespace FluxEncoder.Infrastructure.Tooling;

/// <summary>
/// Parses the human-readable listings FFmpeg prints for <c>-version</c>, <c>-encoders</c>, <c>-decoders</c>,
/// <c>-muxers</c>, <c>-demuxers</c>, <c>-protocols</c>, <c>-filters</c>, and <c>-hwaccels</c>.
/// </summary>
/// <remarks>
/// The listings have a legend, a dashed separator, then one fixed-width row per entry. Rows that do not fit
/// the expected shape are skipped rather than failing the whole snapshot, since the legend text and minor
/// layout details change between FFmpeg releases.
/// </remarks>
public static class FFmpegListingParser
{
    private const string CodecSuffix = "(codec ";

    /// <returns>The version token and configure flags, or <see langword="null"/> when the banner is not FFmpeg's.</returns>
    public static (FFmpegVersion Version, ImmutableArray<string> Configuration)? ParseVersion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        FFmpegVersion? version = null;
        var configuration = ImmutableArray<string>.Empty;

        foreach (var line in Lines(text))
        {
            if (version is null && line.StartsWith("ffmpeg version ", StringComparison.Ordinal))
            {
                var token = line["ffmpeg version ".Length..].Split(' ', 2)[0];
                if (token.Length > 0)
                {
                    version = new FFmpegVersion(token);
                }
            }
            else if (line.StartsWith("configuration:", StringComparison.Ordinal))
            {
                configuration = [.. line["configuration:".Length..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(flag => flag.StartsWith("--", StringComparison.Ordinal))];
            }
        }

        return version is null ? null : (version, configuration);
    }

    /// <summary>
    /// Parses <c>-encoders</c> or <c>-decoders</c>: rows like <c> V....D libx265   libx265 H.265 / HEVC (codec hevc)</c>.
    /// </summary>
    public static FrozenDictionary<string, CodecImplementation> ParseCodecs(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var codecs = new Dictionary<string, CodecImplementation>(StringComparer.Ordinal);

        foreach (var row in RowsAfterSeparator(text))
        {
            // " V....D name  description": a 6-character flag field after one leading space.
            if (row.Length < 9 || row[0] != ' ' || row[7] != ' ')
            {
                continue;
            }

            var flags = row.AsSpan(1, 6);
            MediaStreamKind? kind = flags[0] switch
            {
                'V' => MediaStreamKind.Video,
                'A' => MediaStreamKind.Audio,
                'S' => MediaStreamKind.Subtitle,
                _ => null,
            };

            if (kind is null || SplitNameAndRest(row[8..]) is not var (name, description))
            {
                continue;
            }

            string? codecName = null;
            var suffix = description.LastIndexOf(CodecSuffix, StringComparison.Ordinal);
            if (suffix >= 0 && description.EndsWith(')'))
            {
                codecName = description[(suffix + CodecSuffix.Length)..^1];
                description = description[..suffix].TrimEnd();
            }

            codecs.TryAdd(name, new CodecImplementation(name, kind.Value, description)
            {
                CodecName = codecName,
                IsExperimental = flags[3] == 'X',
            });
        }

        return codecs.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Parses <c>-muxers</c>, <c>-demuxers</c>, or <c>-formats</c>: rows like <c> D   matroska,webm   Matroska / WebM</c>.
    /// </summary>
    public static ImmutableArray<FormatEntry> ParseFormats(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var formats = ImmutableArray.CreateBuilder<FormatEntry>();

        foreach (var row in RowsAfterSeparator(text))
        {
            // " DEd name  description": a 3-character flag field after one leading space.
            if (row.Length < 6 || row[0] != ' ' || row[4] != ' ')
            {
                continue;
            }

            var flags = row.AsSpan(1, 3);
            if (!flags.ContainsAny('D', 'E') || SplitNameAndRest(row[5..]) is not var (names, description))
            {
                continue;
            }

            formats.Add(new FormatEntry(names.Split(','), description, isDevice: flags[2] == 'd'));
        }

        return formats.ToImmutable();
    }

    /// <summary>
    /// Parses <c>-filters</c>: rows like <c> TS scale   V->V   Scale the input video size.</c>.
    /// </summary>
    public static FrozenSet<string> ParseFilters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var filters = new HashSet<string>(StringComparer.Ordinal);
        var inRows = false;

        foreach (var line in Lines(text))
        {
            // The filter legend has no dashed separator; rows start once the legend's "|" line has passed.
            if (!inRows)
            {
                inRows = line.TrimStart().StartsWith("| =", StringComparison.Ordinal);
                continue;
            }

            if (line.Length > 4 && line[0] == ' ' && line[3] == ' ' && SplitNameAndRest(line[4..]) is var (name, rest) && rest.Contains("->", StringComparison.Ordinal))
            {
                filters.Add(name);
            }
        }

        return filters.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Parses <c>-protocols</c>: an <c>Input:</c> list followed by an <c>Output:</c> list.
    /// </summary>
    public static (FrozenSet<string> Input, FrozenSet<string> Output) ParseProtocols(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var input = new HashSet<string>(StringComparer.Ordinal);
        var output = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string>? current = null;

        foreach (var line in Lines(text))
        {
            var trimmed = line.Trim();
            if (trimmed == "Input:")
            {
                current = input;
            }
            else if (trimmed == "Output:")
            {
                current = output;
            }
            else if (current is not null && trimmed.Length > 0 && line.StartsWith(' ') && !trimmed.Contains(' '))
            {
                current.Add(trimmed);
            }
        }

        return (input.ToFrozenSet(StringComparer.Ordinal), output.ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Parses <c>-hwaccels</c>: a header line followed by one method name per line.
    /// </summary>
    public static ImmutableArray<string> ParseHardwareAccelerators(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. Lines(text)
            .SkipWhile(line => !line.StartsWith("Hardware acceleration methods", StringComparison.Ordinal))
            .Skip(1)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.Contains(' '))
            .Distinct(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r'));

    private static IEnumerable<string> RowsAfterSeparator(string text) =>
        Lines(text)
            .SkipWhile(line => !IsSeparator(line))
            .Skip(1)
            .Where(line => line.Trim().Length > 0);

    private static bool IsSeparator(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 2 && trimmed.All(c => c == '-');
    }

    private static (string Name, string Remainder)? SplitNameAndRest(string text)
    {
        var trimmed = text.TrimStart();
        var space = trimmed.IndexOf(' ');
        if (space <= 0)
        {
            return trimmed.Length > 0 ? (trimmed, string.Empty) : null;
        }

        return (trimmed[..space], trimmed[space..].Trim());
    }
}
