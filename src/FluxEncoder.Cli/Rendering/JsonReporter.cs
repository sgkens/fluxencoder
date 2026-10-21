using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// Machine-readable output: exactly one JSON document on stdout, written when the run ends, and nothing else on
/// either stream. Field names are a public contract (see <see cref="SchemaVersion"/>).
/// </summary>
/// <remarks>
/// <code>
/// {
///   "schemaVersion": 1, "dryRun": false, "exitCode": 0,
///   "summary": { "succeeded": 1, "skipped": 0, "failed": 0, "notStarted": 0 },
///   "error": null | { "category": "dependency-missing", "exitCode": 5, "message": "...", "detail": "..." },
///   "notices": [ "settings.json: ..." ],
///   "jobs": [ { "input", "output", "outcome", "elapsedSeconds", "attempts", "videoEncoder", "hardware",
///               "failure": null | {...}, "skipReason": null | "...", "warnings": [ { "code", "message" } ],
///               "commands": [ [ "ffmpeg", "-i", ... ] ] } ]
/// }
/// </code>
/// Category names are the <see cref="FailureCategory"/> names in kebab-case. Commands are redacted.
/// </remarks>
internal sealed class JsonReporter(CliHost host) : IReporter
{
    public const int SchemaVersion = 1;

    private readonly CliHost _host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly List<string> _notices = [];
    private string _ffmpegPath = "ffmpeg";

    public void Notice(string message) => _notices.Add(message);

    public void Fatal(JobFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Write(writer =>
        {
            writer.WriteNumber("exitCode", (int)failure.ExitCode);
            writer.WritePropertyName("error");
            WriteFailure(writer, failure);
            writer.WriteStartArray("jobs");
            writer.WriteEndArray();
        });
    }

    public Task<JobReport> RunJobAsync(int index, int count, string label, Func<IProgress<JobProgress>, Task<JobReport>> run) =>
        run(new Progress<JobProgress>(_ => { }));

    public void Completed(JobReport report, bool dryRun, string ffmpegPath) => _ffmpegPath = ffmpegPath;

    public void Finished(IReadOnlyList<JobReport> reports, BatchResult batch, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(batch);

        Write(writer =>
        {
            writer.WriteBoolean("dryRun", dryRun);
            writer.WriteNumber("exitCode", (int)batch.ExitCode);

            writer.WriteStartObject("summary");
            writer.WriteNumber("succeeded", batch.SucceededCount);
            writer.WriteNumber("skipped", batch.SkippedCount);
            writer.WriteNumber("failed", batch.FailedCount);
            writer.WriteNumber("notStarted", batch.NotStarted);
            writer.WriteEndObject();

            writer.WriteNull("error");

            writer.WriteStartArray("jobs");
            foreach (var report in reports)
            {
                WriteJob(writer, report);
            }

            writer.WriteEndArray();
        });
    }

    private void WriteJob(Utf8JsonWriter writer, JobReport report)
    {
        var result = report.Result;
        writer.WriteStartObject();
        writer.WriteString("input", report.Input);
        writer.WriteString("output", result.OutputPath);
        writer.WriteString("outcome", Kebab(result.Outcome.ToString()));
        writer.WriteNumber("elapsedSeconds", Math.Round(result.Elapsed.TotalSeconds, 3));
        writer.WriteNumber("attempts", result.Attempts);
        WriteNullableString(writer, "videoEncoder", report.VideoEncoder);
        WriteNullableString(writer, "hardware", report.Strategy is null ? null : Kebab(report.Strategy.Vendor.ToString()));

        writer.WritePropertyName("failure");
        if (result.Failure is { } failure)
        {
            WriteFailure(writer, failure);
        }
        else
        {
            writer.WriteNullValue();
        }

        WriteNullableString(writer, "skipReason", result.SkipReason);

        writer.WriteStartArray("warnings");
        foreach (var warning in result.Warnings)
        {
            writer.WriteStartObject();
            writer.WriteString("code", warning.Code);
            writer.WriteString("message", warning.Message);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("commands");
        foreach (var invocation in report.Planned?.Plan?.Invocations ?? [])
        {
            writer.WriteStartArray();
            writer.WriteStringValue(_ffmpegPath);
            foreach (var argument in ArgumentRedactor.Redact(invocation.Arguments))
            {
                writer.WriteStringValue(argument);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFailure(Utf8JsonWriter writer, JobFailure failure)
    {
        writer.WriteStartObject();
        writer.WriteString("category", Kebab(failure.Category.ToString()));
        writer.WriteNumber("exitCode", (int)failure.ExitCode);
        writer.WriteString("message", failure.Message);
        WriteNullableString(writer, "detail", failure.Detail);
        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private void Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            body(writer);

            writer.WriteStartArray("notices");
            foreach (var notice in _notices)
            {
                writer.WriteStringValue(notice);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _host.Out.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
        _host.Out.Flush();
    }

    /// <summary>
    /// <c>InputNotFound</c> to <c>input-not-found</c>.
    /// </summary>
    public static string Kebab(string pascal)
    {
        var builder = new StringBuilder(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(pascal[i - 1]) || (i + 1 < pascal.Length && char.IsLower(pascal[i + 1]))))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
