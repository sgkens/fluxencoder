using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Formats;

/// <summary>
/// A probed file as report rows, rendered to JSON, Markdown, HTML, or an SVG badge (TSKE011.5). Every renderer
/// encodes for its own format, because file names, titles, and tags come from the file and may be hostile.
/// </summary>
/// <remarks>
/// Location and device tags (<see cref="MetadataPrivacy"/>) are left out unless asked for, since exported reports are
/// often shared; <see cref="HiddenTags"/> says how many were left out.
/// </remarks>
public sealed record MediaReport
{
    private MediaReport(MediaInfo media)
    {
        Media = media;
    }

    public MediaInfo Media { get; }

    public IReadOnlyList<(string Label, string Value)> Summary { get; private init; } = [];

    public IReadOnlyList<StreamRow> Streams { get; private init; } = [];

    public IReadOnlyList<KeyValuePair<string, string>> Tags { get; private init; } = [];

    public int HiddenTags { get; private init; }

    public sealed record StreamRow(int Index, string Kind, string Codec, string Details, string Language, string Title, string Flags);

    public static MediaReport Create(MediaInfo media, bool showPrivate = false)
    {
        ArgumentNullException.ThrowIfNull(media);

        var tags = media.Tags.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var shown = showPrivate ? tags : [.. tags.Where(t => !MetadataPrivacy.IsPrivate(t.Key))];

        var summary = new List<(string, string)>
        {
            ("File", media.Source),
            ("Format", media.FormatNames.Count > 0 ? string.Join(", ", media.FormatNames) : "unknown"),
            ("Duration", media.Duration is { } duration ? Clock(duration) : "unknown"),
        };

        if (media.SizeBytes is { } size)
        {
            summary.Add(("Size", Bytes(size)));
        }

        if (media.BitRate is { } bitRate)
        {
            summary.Add(("Bit rate", BitRate(bitRate)));
        }

        summary.Add(("Streams", $"{media.MotionVideoStreams.Count()} video, {media.AudioStreams.Count()} audio, {media.SubtitleStreams.Count()} subtitle, {media.Attachments.Count()} attachment"));
        if (media.Chapters.Count > 0)
        {
            summary.Add(("Chapters", media.Chapters.Count.ToString(CultureInfo.InvariantCulture)));
        }

        return new MediaReport(media)
        {
            Summary = summary,
            Streams = [.. media.Streams.Select(Row)],
            Tags = shown,
            HiddenTags = tags.Count - shown.Count,
        };
    }

    /// <summary>
    /// The short line a badge shows: "1920×1080 HEVC · AAC 6 ch · 1:40:00".
    /// </summary>
    public string Headline()
    {
        var parts = new List<string>();
        if (Media.MotionVideoStreams.FirstOrDefault() is { } video)
        {
            var size = video is { Width: { } w, Height: { } h } ? $"{w}\u00D7{h} " : string.Empty;
            parts.Add(size + (video.CodecName ?? "video").ToUpperInvariant());
        }

        if (Media.AudioStreams.FirstOrDefault() is { } audio)
        {
            parts.Add((audio.CodecName ?? "audio").ToUpperInvariant() + (audio.Channels is { } channels ? $" {channels} ch" : string.Empty));
        }

        if (Media.Duration is { } duration)
        {
            parts.Add(Clock(duration));
        }

        return parts.Count > 0 ? string.Join(" \u00B7 ", parts) : "no streams";
    }

    public JsonObject ToJson()
    {
        var streams = new JsonArray();
        foreach (var stream in Media.Streams)
        {
            var node = new JsonObject
            {
                ["index"] = stream.Index,
                ["kind"] = stream.Kind.ToString().ToLowerInvariant(),
                ["codec"] = stream.CodecName,
                ["language"] = stream.Language?.Value,
                ["title"] = stream.Title,
                ["default"] = stream.IsDefault,
            };

            switch (stream)
            {
                case VideoStream video:
                    node["width"] = video.Width;
                    node["height"] = video.Height;
                    node["frameRate"] = video.AverageFrameRate?.ToString();
                    node["pixelFormat"] = video.PixelFormat;
                    node["bitDepth"] = video.BitDepth;
                    node["hdr"] = new JsonArray([.. HdrNames(video).Select(n => JsonValue.Create(n))]);
                    node["coverArt"] = video.IsAttachedPicture;
                    break;
                case AudioStream audio:
                    node["channels"] = audio.Channels;
                    node["channelLayout"] = audio.ChannelLayout;
                    node["sampleRate"] = audio.SampleRate;
                    break;
                case SubtitleStream subtitle:
                    node["bitmap"] = subtitle.IsBitmap;
                    node["forced"] = subtitle.IsForced;
                    break;
                case AttachmentStream attachment:
                    node["fileName"] = attachment.FileName;
                    node["mimeType"] = attachment.MimeType;
                    break;
            }

            streams.Add(node);
        }

        var chapters = new JsonArray();
        foreach (var chapter in Media.Chapters)
        {
            chapters.Add(new JsonObject
            {
                ["start"] = Seconds(chapter.Start),
                ["end"] = Seconds(chapter.End),
                ["title"] = chapter.Title,
            });
        }

        var tags = new JsonObject();
        foreach (var (key, value) in Tags)
        {
            tags[key] = value;
        }

        return new JsonObject
        {
            ["source"] = Media.Source,
            ["format"] = new JsonArray([.. Media.FormatNames.Select(n => JsonValue.Create(n))]),
            ["duration"] = Media.Duration is { } duration ? Seconds(duration) : null,
            ["sizeBytes"] = Media.SizeBytes,
            ["bitRate"] = Media.BitRate,
            ["streams"] = streams,
            ["chapters"] = chapters,
            ["tags"] = tags,
            ["hiddenTags"] = HiddenTags,
        };
    }

    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.Append("## ").Append(MarkdownText.Cell(Path.GetFileName(Media.Source))).Append("\n\n");
        Table(builder, ["Property", "Value"], Summary.Select(s => new[] { s.Label, s.Value }));
        builder.Append("\n### Streams\n\n");
        Table(builder, ["#", "Type", "Codec", "Details", "Language", "Title", "Flags"], Streams.Select(s => new[] { s.Index.ToString(CultureInfo.InvariantCulture), s.Kind, s.Codec, s.Details, s.Language, s.Title, s.Flags }));

        if (Tags.Count > 0)
        {
            builder.Append("\n### Tags\n\n");
            Table(builder, ["Tag", "Value"], Tags.Select(t => new[] { t.Key, t.Value }));
        }

        if (HiddenTags > 0)
        {
            builder.Append('\n').Append(HiddenNote()).Append('\n');
        }

        return builder.ToString();

        static void Table(StringBuilder builder, string[] header, IEnumerable<string[]> rows)
        {
            builder.Append("| ").AppendJoin(" | ", header).Append(" |\n");
            builder.Append('|').AppendJoin('|', header.Select(_ => " --- ")).Append("|\n");
            foreach (var row in rows)
            {
                builder.Append("| ").AppendJoin(" | ", row.Select(MarkdownText.Cell)).Append(" |\n");
            }
        }
    }

    /// <summary>
    /// A standalone page with a content security policy that forbids scripts and every external resource, so even a
    /// rendering mistake could not run code or contact a server.
    /// </summary>
    public string ToHtml()
    {
        var title = MarkupText.Encode(Path.GetFileName(Media.Source));
        var builder = new StringBuilder();
        builder.Append("""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            """).Append('\n');
        builder.Append("<title>").Append(title).Append("</title>\n");
        builder.Append("""
            <style>
            body { font: 14px/1.5 system-ui, sans-serif; margin: 2rem; color: #1a202c; background: #fff; }
            table { border-collapse: collapse; margin-bottom: 1.5rem; }
            th, td { border: 1px solid #cbd5e0; padding: .25rem .6rem; text-align: left; vertical-align: top; }
            th { background: #edf2f7; }
            td { overflow-wrap: anywhere; }
            @media (prefers-color-scheme: dark) { body { color: #e2e8f0; background: #1a202c; } th { background: #2d3748; } th, td { border-color: #4a5568; } }
            </style>
            </head>
            <body>
            """).Append('\n');
        builder.Append("<h1>").Append(title).Append("</h1>\n");
        Table(builder, ["Property", "Value"], Summary.Select(s => new[] { s.Label, s.Value }));
        builder.Append("<h2>Streams</h2>\n");
        Table(builder, ["#", "Type", "Codec", "Details", "Language", "Title", "Flags"], Streams.Select(s => new[] { s.Index.ToString(CultureInfo.InvariantCulture), s.Kind, s.Codec, s.Details, s.Language, s.Title, s.Flags }));

        if (Tags.Count > 0)
        {
            builder.Append("<h2>Tags</h2>\n");
            Table(builder, ["Tag", "Value"], Tags.Select(t => new[] { t.Key, t.Value }));
        }

        if (HiddenTags > 0)
        {
            builder.Append("<p>").Append(MarkupText.Encode(HiddenNote())).Append("</p>\n");
        }

        builder.Append("</body>\n</html>\n");
        return builder.ToString();

        static void Table(StringBuilder builder, string[] header, IEnumerable<string[]> rows)
        {
            builder.Append("<table>\n<tr>");
            foreach (var cell in header)
            {
                builder.Append("<th>").Append(MarkupText.Encode(cell)).Append("</th>");
            }

            builder.Append("</tr>\n");
            foreach (var row in rows)
            {
                builder.Append("<tr>");
                foreach (var cell in row)
                {
                    builder.Append("<td>").Append(MarkupText.Encode(cell)).Append("</td>");
                }

                builder.Append("</tr>\n");
            }

            builder.Append("</table>\n");
        }
    }

    public string ToBadge() => SvgBadge.Render(Path.GetFileName(Media.Source), Headline());

    public string HiddenNote() =>
        HiddenTags == 1
            ? "1 location or device tag is hidden; --show-private shows it."
            : $"{HiddenTags} location or device tags are hidden; --show-private shows them.";

    private static StreamRow Row(MediaStream stream)
    {
        var details = stream switch
        {
            VideoStream video => string.Join(" \u00B7 ", new[]
            {
                video is { Width: { } w, Height: { } h } ? $"{w}\u00D7{h}" : null,
                video.AverageFrameRate is { } rate && rate.Denominator != 0 ? $"{(double)rate.Numerator / rate.Denominator:0.###} fps" : null,
                video.PixelFormat,
                HdrNames(video).ToList() is { Count: > 0 } hdr ? string.Join("/", hdr) : null,
            }.OfType<string>()),
            AudioStream audio => string.Join(" \u00B7 ", new[]
            {
                audio.Channels is { } channels ? $"{channels} ch" + (audio.ChannelLayout is { } layout ? $" ({layout})" : string.Empty) : null,
                audio.SampleRate is { } rate ? $"{rate / 1000.0:0.#} kHz" : null,
            }.OfType<string>()),
            SubtitleStream subtitle => subtitle.IsBitmap ? "bitmap" : "text",
            AttachmentStream attachment => string.Join(" \u00B7 ", new[] { attachment.FileName, attachment.MimeType }.OfType<string>()),
            _ => string.Empty,
        };

        var flags = new List<string>();
        if (stream.IsDefault)
        {
            flags.Add("default");
        }

        if (stream is SubtitleStream { IsForced: true })
        {
            flags.Add("forced");
        }

        if (stream is VideoStream { IsAttachedPicture: true })
        {
            flags.Add("cover");
        }

        return new StreamRow(
            stream.Index,
            stream is VideoStream { IsAttachedPicture: true } ? "cover" : stream.Kind.ToString().ToLowerInvariant(),
            stream.CodecName ?? "unknown",
            details,
            stream.Language?.Value ?? string.Empty,
            stream.Title ?? string.Empty,
            string.Join(", ", flags));
    }

    private static IEnumerable<string> HdrNames(VideoStream video) =>
        Enum.GetValues<HdrFormat>().Where(f => f != HdrFormat.None && video.HdrFormats.HasFlag(f)).Select(f => f.ToString());

    private static double Seconds(TimeSpan time) => Math.Round(time.TotalSeconds, 3);

    private static string Clock(TimeSpan time) =>
        time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes}:{time.Seconds:00}");

    private static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.##} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0.#} MB"),
        >= 1L << 10 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
    };

    private static string BitRate(long bitsPerSecond) =>
        bitsPerSecond >= 1_000_000
            ? string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond / 1_000_000.0:0.##} Mbit/s")
            : string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond / 1000.0:0} kbit/s");
}
