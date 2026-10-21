using System.Globalization;
using System.Text.Json;
using FluxEncoder.Core.Media;

namespace FluxEncoder.Infrastructure.Probing;

/// <summary>
/// Converts FFprobe <c>-print_format json</c> output into <see cref="MediaInfo"/>.
/// </summary>
/// <remarks>
/// Parsing is tolerant: every field is optional, numbers may arrive as JSON numbers or strings, and
/// <c>N/A</c>, <c>0/0</c>, and malformed values become <see langword="null"/> instead of failing the probe.
/// All numeric text is parsed with the invariant culture. Uses <see cref="JsonDocument"/> rather than
/// reflection-based serialization so it stays Native AOT safe.
/// </remarks>
public static class FFprobeJsonParser
{
    private static readonly (string Key, StreamDisposition Flag)[] DispositionKeys =
    [
        ("default", StreamDisposition.Default),
        ("dub", StreamDisposition.Dub),
        ("original", StreamDisposition.Original),
        ("comment", StreamDisposition.Comment),
        ("lyrics", StreamDisposition.Lyrics),
        ("karaoke", StreamDisposition.Karaoke),
        ("forced", StreamDisposition.Forced),
        ("hearing_impaired", StreamDisposition.HearingImpaired),
        ("visual_impaired", StreamDisposition.VisualImpaired),
        ("clean_effects", StreamDisposition.CleanEffects),
        ("attached_pic", StreamDisposition.AttachedPicture),
        ("timed_thumbnails", StreamDisposition.TimedThumbnails),
        ("non_diegetic", StreamDisposition.NonDiegetic),
        ("captions", StreamDisposition.Captions),
        ("descriptions", StreamDisposition.Descriptions),
        ("metadata", StreamDisposition.Metadata),
        ("dependent", StreamDisposition.Dependent),
        ("still_image", StreamDisposition.StillImage),
    ];

    /// <exception cref="FormatException">The text is not a JSON object.</exception>
    public static MediaInfo Parse(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        using var document = ParseDocument(json);
        var root = document.RootElement;

        var format = Child(root, "format");
        var streams = new List<MediaStream>();

        if (Child(root, "streams") is { ValueKind: JsonValueKind.Array } streamArray)
        {
            var position = 0;
            foreach (var element in streamArray.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    streams.Add(ParseStream(element, position));
                }

                position++;
            }
        }

        var chapters = new List<MediaChapter>();
        if (Child(root, "chapters") is { ValueKind: JsonValueKind.Array } chapterArray)
        {
            foreach (var element in chapterArray.EnumerateArray())
            {
                if (ParseChapter(element) is { } chapter)
                {
                    chapters.Add(chapter);
                }
            }
        }

        return new MediaInfo
        {
            Source = source,
            FormatNames = format is { } f && GetString(f, "format_name") is { } names
                ? [.. names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                : [],
            Duration = format is { } fd ? GetSeconds(fd, "duration") : null,
            StartTime = format is { } fs ? GetSeconds(fs, "start_time") : null,
            SizeBytes = format is { } fz ? GetLong(fz, "size") : null,
            BitRate = format is { } fb ? GetLong(fb, "bit_rate") : null,
            Tags = format is { } ft ? GetTags(ft) : MediaTags.Empty,
            Streams = streams,
            Chapters = chapters,
        };
    }

    /// <summary>
    /// Reads HDR side data from a <c>-show_frames</c> probe of the first video frame. MP4 and raw HEVC carry
    /// mastering display and HDR10+ metadata per frame rather than per stream.
    /// </summary>
    /// <returns><paramref name="existing"/> with any frame-level metadata added; stream-level values win.</returns>
    public static HdrInfo MergeFrameSideData(HdrInfo existing, string framesJson)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(framesJson);

        using var document = ParseDocument(framesJson);

        var merged = existing;
        if (Child(document.RootElement, "frames") is { ValueKind: JsonValueKind.Array } frames)
        {
            foreach (var frame in frames.EnumerateArray())
            {
                var fromFrame = ParseHdr(frame);
                merged = merged with
                {
                    MasteringDisplay = merged.MasteringDisplay ?? fromFrame.MasteringDisplay,
                    ContentLightLevel = merged.ContentLightLevel ?? fromFrame.ContentLightLevel,
                    HasHdr10PlusMetadata = merged.HasHdr10PlusMetadata || fromFrame.HasHdr10PlusMetadata,
                    HasDolbyVisionConfiguration = merged.HasDolbyVisionConfiguration || fromFrame.HasDolbyVisionConfiguration,
                };
            }
        }

        return merged;
    }

    private static JsonDocument ParseDocument(string json)
    {
        try
        {
            var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new FormatException("FFprobe output is not a JSON object.");
            }

            return document;
        }
        catch (JsonException ex)
        {
            throw new FormatException("FFprobe output is not valid JSON.", ex);
        }
    }

    private static MediaStream ParseStream(JsonElement element, int position)
    {
        var index = GetInt(element, "index") is { } i and >= 0 ? i : position;
        var tags = GetTags(element);

        MediaStream stream = GetString(element, "codec_type") switch
        {
            "video" => ParseVideo(element, tags, index),
            "audio" => new AudioStream
            {
                Index = index,
                Channels = Positive(GetInt(element, "channels")),
                ChannelLayout = GetString(element, "channel_layout"),
                SampleRate = Positive(GetInt(element, "sample_rate")),
                SampleFormat = GetString(element, "sample_fmt"),
            },
            "subtitle" => new SubtitleStream { Index = index },
            "attachment" => new AttachmentStream
            {
                Index = index,
                FileName = tags.GetValueOrDefault("filename"),
                MimeType = tags.GetValueOrDefault("mimetype"),
            },
            _ => new DataStream { Index = index },
        };

        return stream with
        {
            CodecName = GetString(element, "codec_name"),
            Language = LanguageTag.TryParse(tags.GetValueOrDefault("language"), out var language) ? language : null,
            Title = tags.GetValueOrDefault("title") is { Length: > 0 } title ? title : null,
            Disposition = ParseDisposition(element),
            BitRate = GetLong(element, "bit_rate") is { } bitRate and > 0 ? bitRate : null,
            Duration = GetSeconds(element, "duration") ?? ParseClock(tags.GetValueOrDefault("DURATION")),
            Tags = tags,
        };
    }

    private static VideoStream ParseVideo(JsonElement element, IReadOnlyDictionary<string, string> tags, int index)
    {
        var pixelFormat = GetString(element, "pix_fmt");

        return new VideoStream
        {
            Index = index,
            Width = Positive(GetInt(element, "width")),
            Height = Positive(GetInt(element, "height")),
            PixelFormat = pixelFormat,
            BitDepth = Positive(GetInt(element, "bits_per_raw_sample")) ?? BitDepthFromPixelFormat(pixelFormat),
            AverageFrameRate = Rational.TryParse(GetString(element, "avg_frame_rate"), out var average) ? average : null,
            BaseFrameRate = Rational.TryParse(GetString(element, "r_frame_rate"), out var baseRate) ? baseRate : null,
            SampleAspectRatio = Rational.TryParse(GetString(element, "sample_aspect_ratio"), out var sar) ? sar : null,
            Rotation = ParseRotation(element, tags),
            Color = new ColorInfo
            {
                Range = GetString(element, "color_range"),
                Primaries = GetString(element, "color_primaries"),
                Transfer = GetString(element, "color_transfer"),
                Matrix = GetString(element, "color_space"),
                ChromaLocation = GetString(element, "chroma_location"),
            },
            Hdr = ParseHdr(element),
        };
    }

    private static int? ParseRotation(JsonElement element, IReadOnlyDictionary<string, string> tags)
    {
        foreach (var sideData in SideData(element))
        {
            if (SideDataType(sideData).Contains("Display Matrix", StringComparison.OrdinalIgnoreCase)
                && GetDouble(sideData, "rotation") is { } rotation)
            {
                return (int)Math.Round(rotation);
            }
        }

        // Pre-5.0 FFmpeg reported rotation as a tag.
        return int.TryParse(tags.GetValueOrDefault("rotate"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var tagged)
            ? tagged
            : null;
    }

    private static HdrInfo ParseHdr(JsonElement element)
    {
        var hdr = HdrInfo.None;

        foreach (var sideData in SideData(element))
        {
            var type = SideDataType(sideData);

            if (type.Contains("Mastering display", StringComparison.OrdinalIgnoreCase))
            {
                hdr = hdr with { MasteringDisplay = ParseMasteringDisplay(sideData) ?? hdr.MasteringDisplay };
            }
            else if (type.Contains("Content light level", StringComparison.OrdinalIgnoreCase)
                && GetInt(sideData, "max_content") is { } maxContent
                && GetInt(sideData, "max_average") is { } maxAverage)
            {
                hdr = hdr with { ContentLightLevel = new ContentLightLevel(maxContent, maxAverage) };
            }
            else if (type.Contains("SMPTE2094-40", StringComparison.OrdinalIgnoreCase))
            {
                hdr = hdr with { HasHdr10PlusMetadata = true };
            }
            else if (type.Contains("DOVI configuration", StringComparison.OrdinalIgnoreCase))
            {
                hdr = hdr with { HasDolbyVisionConfiguration = true };
            }
        }

        return hdr;
    }

    private static MasteringDisplay? ParseMasteringDisplay(JsonElement sideData)
    {
        Rational? Get(string name) => Rational.TryParse(GetString(sideData, name), out var value) ? value : null;

        if (Get("red_x") is not { } redX || Get("red_y") is not { } redY
            || Get("green_x") is not { } greenX || Get("green_y") is not { } greenY
            || Get("blue_x") is not { } blueX || Get("blue_y") is not { } blueY
            || Get("white_point_x") is not { } whiteX || Get("white_point_y") is not { } whiteY
            || Get("min_luminance") is not { } minLuminance || Get("max_luminance") is not { } maxLuminance)
        {
            return null;
        }

        return new MasteringDisplay
        {
            RedX = redX,
            RedY = redY,
            GreenX = greenX,
            GreenY = greenY,
            BlueX = blueX,
            BlueY = blueY,
            WhitePointX = whiteX,
            WhitePointY = whiteY,
            MinLuminance = minLuminance,
            MaxLuminance = maxLuminance,
        };
    }

    private static MediaChapter? ParseChapter(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || GetSeconds(element, "start_time") is not { } start
            || GetSeconds(element, "end_time") is not { } end
            || start < TimeSpan.Zero
            || end < start)
        {
            return null;
        }

        var tags = GetTags(element);
        return new MediaChapter(start, end)
        {
            Title = tags.GetValueOrDefault("title") is { Length: > 0 } title ? title : null,
            Tags = tags,
        };
    }

    private static StreamDisposition ParseDisposition(JsonElement element)
    {
        if (Child(element, "disposition") is not { ValueKind: JsonValueKind.Object } disposition)
        {
            return StreamDisposition.None;
        }

        var flags = StreamDisposition.None;
        foreach (var (key, flag) in DispositionKeys)
        {
            if (GetInt(disposition, key) is > 0)
            {
                flags |= flag;
            }
        }

        return flags;
    }

    private static int? BitDepthFromPixelFormat(string? pixelFormat)
    {
        // Planar formats name their depth after a 'p' (yuv420p10le, gbrp12, p010le) and grey formats after
        // "gray" (gray10le). Others are left unknown: nv12 and rgb24 use the digits for something else.
        if (string.IsNullOrEmpty(pixelFormat))
        {
            return null;
        }

        var span = pixelFormat.AsSpan();
        if (span.EndsWith("le") || span.EndsWith("be"))
        {
            span = span[..^2];
        }

        var digits = 0;
        while (digits < span.Length && char.IsAsciiDigit(span[^(digits + 1)]))
        {
            digits++;
        }

        if (digits == 0 || digits == span.Length)
        {
            return null;
        }

        var prefix = span[..^digits];
        if (!prefix.EndsWith("p") && !prefix.SequenceEqual("gray"))
        {
            return null;
        }

        var depth = int.Parse(span[^digits..], CultureInfo.InvariantCulture);
        return depth is >= 8 and <= 16 ? depth : null;
    }

    /// <summary>
    /// Parses Matroska's <c>HH:MM:SS.nnnnnnnnn</c> DURATION tag, whose nine fractional digits exceed what
    /// <see cref="TimeSpan.TryParseExact(string?, string?, IFormatProvider?, out TimeSpan)"/> accepts.
    /// </summary>
    private static TimeSpan? ParseClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || minutes >= 60
            || seconds >= 60)
        {
            return null;
        }

        var total = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return total > TimeSpan.Zero ? total : null;
    }

    private static IEnumerable<JsonElement> SideData(JsonElement element) =>
        Child(element, "side_data_list") is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
            : [];

    private static string SideDataType(JsonElement sideData) => GetString(sideData, "side_data_type") ?? string.Empty;

    private static IReadOnlyDictionary<string, string> GetTags(JsonElement element)
    {
        if (Child(element, "tags") is not { ValueKind: JsonValueKind.Object } tags)
        {
            return MediaTags.Empty;
        }

        return MediaTags.Create(tags.EnumerateObject()
            .Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            .Select(p => KeyValuePair.Create(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText())));
    }

    private static JsonElement? Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static string? GetString(JsonElement element, string name)
    {
        var text = Child(element, name) switch
        {
            { ValueKind: JsonValueKind.String } value => value.GetString(),
            { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) || text is "N/A" or "unknown" ? null : text;
    }

    private static double? GetDouble(JsonElement element, string name) =>
        double.TryParse(GetString(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;

    private static long? GetLong(JsonElement element, string name) =>
        long.TryParse(GetString(element, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? GetInt(JsonElement element, string name) =>
        int.TryParse(GetString(element, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Positive(int? value) => value > 0 ? value : null;

    private static TimeSpan? GetSeconds(JsonElement element, string name) =>
        GetDouble(element, name) is { } seconds && Math.Abs(seconds) < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
