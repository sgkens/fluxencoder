using System.CommandLine;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Formats;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;
using FluxEncoder.Infrastructure.Configuration;
using FluxEncoder.Infrastructure.Manifests;
using FluxEncoder.Infrastructure.Probing;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Cli.Commands;

/// <summary>
/// <c>info</c>: what a media file contains, as terminal text or as a JSON, YAML, Markdown, HTML, or SVG export
/// (TSKE011.5). Exports encode every value for their format, so a hostile title or tag stays text.
/// </summary>
internal static class InfoCommand
{
    public const string Name = "info";

    private static readonly string[] Formats = ["text", "json", "yaml", "markdown", "html", "svg"];

    public static Command Build(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var input = new Argument<string>("input") { Description = "A media file or URL." };
        var format = new Option<string>("--format", "-f")
        {
            Description = "text for the terminal; json, yaml, markdown, or html for a report; svg for a summary badge.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong(Formats);
        var showPrivate = new Option<bool>("--show-private") { Description = "Include location and recording-device tags, which are hidden by default." };
        var ffprobe = new Option<string?>("--ffprobe") { Description = "FFprobe executable or its directory." };

        var command = new Command(Name, "Show the streams, tags, and chapters of a media file, or export them as a report.")
        {
            input, format, showPrivate, ffprobe,
        };

        command.SetAction(async (parse, token) =>
        {
            var source = parse.GetValue(input)!;
            if (RemoteMount.SplitRemoteInput(source) is not null)
            {
                return await FailAsync(host, new JobFailure(FailureCategory.InvalidUsage, "info does not read remote:// paths yet; give the file or its URL.")).ConfigureAwait(false);
            }

            var tools = FFmpegLocator.Locate(new ToolPathOverrides(null, parse.GetValue(ffprobe)), ToolEnvironment.Current);
            if (tools.FFprobe is null)
            {
                return await FailAsync(host, new JobFailure(FailureCategory.DependencyMissing, "FFprobe was not found. Install FFmpeg or pass --ffprobe.")).ConfigureAwait(false);
            }

            var (settings, _) = SettingsStore.Load(host.Paths.SettingsFile);
            var probed = await MediaProber.ProbeAsync(tools.FFprobe.Path, source, token, settings.Security).ConfigureAwait(false);
            if (!probed.IsSuccess)
            {
                return await FailAsync(host, probed.Failure).ConfigureAwait(false);
            }

            // The source is shown as given, minus any credentials in it.
            var media = probed.Value.Media with { Source = Redaction.Text(source) };
            var report = MediaReport.Create(media, parse.GetValue(showPrivate));
            var text = parse.GetValue(format) switch
            {
                "json" => Json(report),
                "yaml" => YamlWriter.Write(report.ToJson()),
                "markdown" => report.ToMarkdown(),
                "html" => report.ToHtml(),
                "svg" => report.ToBadge(),
                _ => Text(report),
            };

            await host.Out.WriteAsync(text).ConfigureAwait(false);
            return (int)ExitCode.Success;
        });

        return command;
    }

    private static async Task<int> FailAsync(CliHost host, JobFailure failure)
    {
        await host.Error.WriteLineAsync(TerminalText.Safe(failure.Message)).ConfigureAwait(false);
        return (int)failure.ExitCode;
    }

    private static string Json(MediaReport report)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            report.ToJson().WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>
    /// Aligned columns for the terminal. Cells are made safe first and padded after, so control characters in a
    /// title can neither move the cursor nor break the alignment.
    /// </summary>
    private static string Text(MediaReport report)
    {
        var builder = new StringBuilder();
        var labelWidth = report.Summary.Max(s => s.Label.Length);
        foreach (var (label, value) in report.Summary)
        {
            builder.Append(label.PadRight(labelWidth + 2)).Append(TerminalText.Safe(value)).Append('\n');
        }

        builder.Append("\nStreams\n");
        Columns(builder, report.Streams.Select(s => new[] { $"#{s.Index}", s.Kind, s.Codec, s.Details, s.Language, s.Title, s.Flags }));

        if (report.Tags.Count > 0)
        {
            builder.Append("\nTags\n");
            Columns(builder, report.Tags.Select(t => new[] { t.Key, t.Value }));
        }

        if (report.HiddenTags > 0)
        {
            builder.Append('\n').Append(report.HiddenNote()).Append('\n');
        }

        return builder.ToString();

        static void Columns(StringBuilder builder, IEnumerable<string[]> rows)
        {
            var safe = rows.Select(r => r.Select(c => TerminalText.Safe(c).Replace('\n', ' ')).ToArray()).ToList();
            if (safe.Count == 0)
            {
                builder.Append("  (none)\n");
                return;
            }

            var widths = Enumerable.Range(0, safe[0].Length).Select(i => safe.Max(r => DisplayWidth.Of(r[i]))).ToArray();
            foreach (var row in safe)
            {
                builder.Append("  ").Append(string.Join("  ", row.Select((cell, i) => DisplayWidth.PadRight(cell, widths[i]))).TrimEnd()).Append('\n');
            }
        }
    }
}
