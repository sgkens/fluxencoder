using System.Globalization;
using System.Text.Json;
using FluxEncoder.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;

namespace FluxEncoder.Infrastructure.History;

/// <summary>
/// Records, lists, and deletes runs. Each run and its samples are written in one transaction, together with
/// retention pruning, so history is never left half-written and never grows without bound (TSKE009.5).
/// </summary>
public sealed class HistoryStore(HistoryDatabase database)
{
    private readonly HistoryDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task RecordAsync(RunRecord run, IReadOnlyList<TelemetrySample> samples, HistorySettings retention, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(retention);

        await _database.WriteAsync(async (connection, transaction) =>
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO runs (id, started_utc, finished_utc, input, output, outcome, exit_code, failure_category,
                                      failure_message, video_encoder, hardware, attempts, elapsed_seconds, input_bytes,
                                      output_bytes, command, warnings)
                    VALUES ($id, $started, $finished, $input, $output, $outcome, $exit, $category, $message, $encoder,
                            $hardware, $attempts, $elapsed, $inputBytes, $outputBytes, $command, $warnings);
                    """;
                insert.Parameters.AddWithValue("$id", run.Id.ToString("D"));
                insert.Parameters.AddWithValue("$started", RunRecord.FormatTimestamp(run.StartedUtc));
                insert.Parameters.AddWithValue("$finished", RunRecord.FormatTimestamp(run.FinishedUtc));
                insert.Parameters.AddWithValue("$input", run.Input);
                insert.Parameters.AddWithValue("$output", run.Output);
                insert.Parameters.AddWithValue("$outcome", run.Outcome);
                insert.Parameters.AddWithValue("$exit", run.ExitCode);
                insert.Parameters.AddWithValue("$category", (object?)run.FailureCategory ?? DBNull.Value);
                insert.Parameters.AddWithValue("$message", (object?)run.FailureMessage ?? DBNull.Value);
                insert.Parameters.AddWithValue("$encoder", (object?)run.VideoEncoder ?? DBNull.Value);
                insert.Parameters.AddWithValue("$hardware", (object?)run.Hardware ?? DBNull.Value);
                insert.Parameters.AddWithValue("$attempts", run.Attempts);
                insert.Parameters.AddWithValue("$elapsed", run.ElapsedSeconds);
                insert.Parameters.AddWithValue("$inputBytes", (object?)run.InputBytes ?? DBNull.Value);
                insert.Parameters.AddWithValue("$outputBytes", (object?)run.OutputBytes ?? DBNull.Value);
                insert.Parameters.AddWithValue("$command", (object?)run.Command ?? DBNull.Value);
                insert.Parameters.AddWithValue("$warnings", JsonSerializer.Serialize(run.WarningCodes.ToArray(), HistoryJsonContext.Default.StringArray));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (samples.Count > 0)
            {
                await using var sample = connection.CreateCommand();
                sample.Transaction = transaction;
                sample.CommandText = """
                    INSERT OR REPLACE INTO samples (run_id, elapsed_seconds, processed_seconds, fps, speed, bitrate_kbps,
                                                    size_bytes, cpu_percent, memory_bytes, dropped_frames)
                    VALUES ($run, $elapsed, $processed, $fps, $speed, $bitrate, $size, $cpu, $memory, $dropped);
                    """;
                var parameters = new[] { "$run", "$elapsed", "$processed", "$fps", "$speed", "$bitrate", "$size", "$cpu", "$memory", "$dropped" }
                    .Select(name => sample.Parameters.Add(name, SqliteType.Text))
                    .ToArray();
                sample.Prepare();

                foreach (var s in samples)
                {
                    object?[] values = [run.Id.ToString("D"), s.ElapsedSeconds, s.ProcessedSeconds, s.FramesPerSecond, s.Speed, s.BitRateKbps, s.SizeBytes, s.CpuPercent, s.MemoryBytes, s.DroppedFrames];
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        parameters[i].SqliteType = values[i] switch
                        {
                            string => SqliteType.Text,
                            long or int => SqliteType.Integer,
                            double => SqliteType.Real,
                            _ => SqliteType.Text,
                        };
                        parameters[i].Value = values[i] ?? DBNull.Value;
                    }

                    await sample.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await PruneAsync(connection, transaction, retention, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return 0;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The most recent runs, newest first.
    /// </summary>
    public async Task<IReadOnlyList<RunRecord>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT id, started_utc, finished_utc, input, output, outcome, exit_code, failure_category, failure_message,
                   video_encoder, hardware, attempts, elapsed_seconds, input_bytes, output_bytes, command, warnings
            FROM runs ORDER BY started_utc DESC, id DESC LIMIT $limit;
            """;
        query.Parameters.AddWithValue("$limit", limit);

        var runs = new List<RunRecord>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            runs.Add(new RunRecord
            {
                Id = Guid.Parse(reader.GetString(0)),
                StartedUtc = RunRecord.ParseTimestamp(reader.GetString(1)),
                FinishedUtc = RunRecord.ParseTimestamp(reader.GetString(2)),
                Input = reader.GetString(3),
                Output = reader.GetString(4),
                Outcome = reader.GetString(5),
                ExitCode = reader.GetInt32(6),
                FailureCategory = reader.IsDBNull(7) ? null : reader.GetString(7),
                FailureMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                VideoEncoder = reader.IsDBNull(9) ? null : reader.GetString(9),
                Hardware = reader.IsDBNull(10) ? null : reader.GetString(10),
                Attempts = reader.GetInt32(11),
                ElapsedSeconds = reader.GetDouble(12),
                InputBytes = reader.IsDBNull(13) ? null : reader.GetInt64(13),
                OutputBytes = reader.IsDBNull(14) ? null : reader.GetInt64(14),
                Command = reader.IsDBNull(15) ? null : reader.GetString(15),
                WarningCodes = [.. JsonSerializer.Deserialize(reader.GetString(16), HistoryJsonContext.Default.StringArray) ?? []],
            });
        }

        return runs;
    }

    public async Task<IReadOnlyList<TelemetrySample>> SamplesAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT elapsed_seconds, processed_seconds, fps, speed, bitrate_kbps, size_bytes, cpu_percent, memory_bytes, dropped_frames
            FROM samples WHERE run_id = $run ORDER BY elapsed_seconds;
            """;
        query.Parameters.AddWithValue("$run", runId.ToString("D"));

        var samples = new List<TelemetrySample>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            double? Real(int i) => reader.IsDBNull(i) ? null : reader.GetDouble(i);
            long? Integer(int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);
            samples.Add(new TelemetrySample(reader.GetDouble(0), Real(1), Real(2), Real(3), Real(4), Integer(5), Real(6), Integer(7), Integer(8)));
        }

        return samples;
    }

    /// <summary>
    /// Deletes runs (and their samples) that started before <paramref name="olderThan"/>, or all runs when it is
    /// <see langword="null"/>.
    /// </summary>
    /// <returns>The number of runs deleted.</returns>
    public Task<int> DeleteAsync(DateTimeOffset? olderThan, CancellationToken cancellationToken = default) =>
        _database.WriteAsync(async (connection, transaction) =>
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = olderThan is null ? "DELETE FROM runs;" : "DELETE FROM runs WHERE started_utc < $cutoff;";
            if (olderThan is { } cutoff)
            {
                delete.Parameters.AddWithValue("$cutoff", RunRecord.FormatTimestamp(cutoff));
            }

            return await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// Applies the retention rules inside the caller's transaction: runs older than the retention period, then the
    /// oldest runs beyond the run limit. Samples go with their runs through the foreign key cascade.
    /// </summary>
    private static async Task PruneAsync(SqliteConnection connection, SqliteTransaction transaction, HistorySettings retention, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var prune = connection.CreateCommand();
        prune.Transaction = transaction;
        prune.CommandText = """
            DELETE FROM runs WHERE started_utc < $cutoff;
            DELETE FROM runs WHERE id IN (SELECT id FROM runs ORDER BY started_utc DESC, id DESC LIMIT -1 OFFSET $max);
            """;
        prune.Parameters.AddWithValue("$cutoff", RunRecord.FormatTimestamp(now - TimeSpan.FromDays(retention.RetentionDays)));
        prune.Parameters.AddWithValue("$max", retention.MaxRuns);
        await prune.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
