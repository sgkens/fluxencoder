using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.History;
using Microsoft.Data.Sqlite;

namespace FluxEncoder.Cli.Commands;

/// <summary>
/// <c>history</c>: the local record of past runs.
/// </summary>
internal static class HistoryCommand
{
    public static Command Build(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var limit = new Option<int>("--limit", "-n") { Description = "How many runs to show.", DefaultValueFactory = _ => 20 };
        limit.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() is < 1 or > 10_000)
            {
                result.AddError("--limit must be between 1 and 10000.");
            }
        });

        var json = new Option<bool>("--json") { Description = "Write the runs as JSON." };
        var list = new Command("list", "Show recent runs, newest first.") { limit, json };
        list.SetAction(async (result, token) =>
        {
            if (!File.Exists(host.Paths.HistoryDatabase))
            {
                await (result.GetValue(json) ? host.Out.WriteLineAsync("[]") : host.Error.WriteLineAsync("No runs recorded yet.")).ConfigureAwait(false);
                return (int)ExitCode.Success;
            }

            var (ok, runs) = await WithStoreAsync(host, store => store.ListAsync(result.GetValue(limit), token)).ConfigureAwait(false);
            if (!ok || runs is null)
            {
                return (int)ExitCode.GeneralFailure;
            }

            await host.Out.WriteAsync(result.GetValue(json) ? Json(runs) : Table(runs)).ConfigureAwait(false);
            return (int)ExitCode.Success;
        });

        var olderThan = new Option<int?>("--older-than") { Description = "Only delete runs older than this many days." };
        var clear = new Command("clear", "Delete recorded runs and their samples.") { olderThan };
        clear.SetAction(async (result, token) =>
        {
            if (!File.Exists(host.Paths.HistoryDatabase))
            {
                await host.Out.WriteLineAsync("Deleted 0 runs.").ConfigureAwait(false);
                return (int)ExitCode.Success;
            }

            DateTimeOffset? cutoff = result.GetValue(olderThan) is { } days ? DateTimeOffset.UtcNow - TimeSpan.FromDays(days) : null;
            var (ok, deleted) = await WithStoreAsync(host, store => store.DeleteAsync(cutoff, token)).ConfigureAwait(false);
            if (!ok)
            {
                return (int)ExitCode.GeneralFailure;
            }

            await host.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Deleted {deleted} run{(deleted == 1 ? string.Empty : "s")}.")).ConfigureAwait(false);
            return (int)ExitCode.Success;
        });

        return new Command("history", "Show or delete the local record of past runs.") { list, clear };
    }

    private static async Task<(bool Ok, T? Value)> WithStoreAsync<T>(CliHost host, Func<HistoryStore, Task<T>> action)
    {
        try
        {
            var (database, notice) = await HistoryDatabase.OpenAsync(host.Paths.HistoryDatabase).ConfigureAwait(false);
            if (notice is not null)
            {
                await host.Error.WriteLineAsync(notice).ConfigureAwait(false);
            }

            return (true, await action(new HistoryStore(database)).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await host.Error.WriteLineAsync($"Run history could not be read: {ex.Message}").ConfigureAwait(false);
            return (false, default);
        }
    }

    private static string Table(IReadOnlyList<RunRecord> runs)
    {
        if (runs.Count == 0)
        {
            return "No runs recorded yet.\n";
        }

        var rows = runs.Select(r => new[]
        {
            r.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            r.Outcome,
            r.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s",
            r.VideoEncoder ?? "-",
            $"{Path.GetFileName(r.Input)} -> {r.Output}",
        }).ToList();

        string[] header = ["started", "outcome", "time", "encoder", "input -> output"];
        var widths = Enumerable.Range(0, header.Length - 1).Select(i => rows.Max(row => row[i].Length)).Select((w, i) => Math.Max(w, header[i].Length)).ToArray();

        // Columns are padded on plain text; nothing here is styled, so widths stay exact.
        var builder = new StringBuilder();
        foreach (var row in rows.Prepend(header))
        {
            for (var i = 0; i < row.Length - 1; i++)
            {
                builder.Append(row[i].PadRight(widths[i] + 2));
            }

            builder.Append(row[^1]).Append('\n');
        }

        return builder.ToString();
    }

    private static string Json(IReadOnlyList<RunRecord> runs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var run in runs)
            {
                writer.WriteStartObject();
                writer.WriteString("id", run.Id.ToString("D"));
                writer.WriteString("startedUtc", RunRecord.FormatTimestamp(run.StartedUtc));
                writer.WriteString("finishedUtc", RunRecord.FormatTimestamp(run.FinishedUtc));
                writer.WriteString("input", run.Input);
                writer.WriteString("output", run.Output);
                writer.WriteString("outcome", run.Outcome);
                writer.WriteNumber("exitCode", run.ExitCode);
                WriteNullable(writer, "failureCategory", run.FailureCategory);
                WriteNullable(writer, "failureMessage", run.FailureMessage);
                WriteNullable(writer, "videoEncoder", run.VideoEncoder);
                WriteNullable(writer, "hardware", run.Hardware);
                writer.WriteNumber("attempts", run.Attempts);
                writer.WriteNumber("elapsedSeconds", run.ElapsedSeconds);
                WriteNullableNumber(writer, "inputBytes", run.InputBytes);
                WriteNullableNumber(writer, "outputBytes", run.OutputBytes);
                WriteNullable(writer, "command", run.Command);
                writer.WriteStartArray("warnings");
                foreach (var code in run.WarningCodes)
                {
                    writer.WriteStringValue(code);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
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

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }
}
