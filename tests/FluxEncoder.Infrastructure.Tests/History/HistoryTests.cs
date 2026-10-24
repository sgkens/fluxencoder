using System.Diagnostics;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Infrastructure.Configuration;
using FluxEncoder.Infrastructure.History;
using Microsoft.Data.Sqlite;

namespace FluxEncoder.Infrastructure.Tests.History;

public sealed class HistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-history-" + Guid.NewGuid().ToString("N"));

    public HistoryTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private string DatabasePath => Path.Combine(_directory, "data", "history.db");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HistoryStore> StoreAsync()
    {
        var (database, notice) = await HistoryDatabase.OpenAsync(DatabasePath, Token);
        Assert.Null(notice);
        return new HistoryStore(database);
    }

    private static RunRecord Run(DateTimeOffset started, string input = "in.mkv") => new()
    {
        Id = RunRecord.NewId(),
        StartedUtc = started,
        FinishedUtc = started + TimeSpan.FromSeconds(5),
        Input = input,
        Output = "out.mkv",
        Outcome = "succeeded",
        ExitCode = 0,
        VideoEncoder = "libx264",
        Hardware = "cpu",
        ElapsedSeconds = 5,
        Command = "ffmpeg -i in.mkv out.mkv",
        WarningCodes = ["low-disk-space"],
    };

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Token);
    }

    [Fact]
    public async Task Open_CreatesMigratedWalDatabase()
    {
        await StoreAsync();

        Assert.Equal(1L, await ScalarAsync("PRAGMA user_version;"));
        Assert.Equal("wal", await ScalarAsync("PRAGMA journal_mode;"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(DatabasePath));
        }
    }

    [Fact]
    public async Task Connections_EnforceForeignKeys()
    {
        var (database, _) = await HistoryDatabase.OpenAsync(DatabasePath, Token);

        await Assert.ThrowsAsync<SqliteException>(() => database.WriteAsync(async (connection, transaction) =>
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO samples (run_id, elapsed_seconds) VALUES ('missing', 1);";
            return await insert.ExecuteNonQueryAsync(Token);
        }, Token));
    }

    [Fact]
    public async Task Record_RoundTripsRunAndSamples()
    {
        var store = await StoreAsync();
        var started = new DateTimeOffset(2026, 10, 21, 10, 0, 0, 123, TimeSpan.Zero);
        var run = Run(started) with { InputBytes = 1000, OutputBytes = 400, FailureCategory = null };
        TelemetrySample[] samples =
        [
            new(0.5, 1.0, 25, 2.0, 800, 1000, 12.5, 50_000_000, 0),
            new(1.5, 3.0, null, null, null, null, null, null, null),
        ];

        await store.RecordAsync(run, samples, HistorySettings.Default, Token);

        var listed = Assert.Single(await store.ListAsync(10, Token));
        Assert.Equal(run, listed);
        Assert.Equal(samples, await store.SamplesAsync(run.Id, Token));
    }

    [Fact]
    public async Task Ids_AreVersion7AndTimestampsAreIsoUtc()
    {
        var id = RunRecord.NewId();

        Assert.Equal(7, id.Version);
        Assert.Equal("2026-10-21T09:05:03.1234567Z", RunRecord.FormatTimestamp(new DateTimeOffset(2026, 10, 21, 20, 5, 3, TimeSpan.FromHours(11)).AddTicks(1234567)));
    }

    [Fact]
    public async Task List_IsNewestFirst()
    {
        var store = await StoreAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RecordAsync(Run(now - TimeSpan.FromHours(2), "old.mkv"), [], HistorySettings.Default, Token);
        await store.RecordAsync(Run(now, "new.mkv"), [], HistorySettings.Default, Token);

        Assert.Equal(["new.mkv", "old.mkv"], (await store.ListAsync(10, Token)).Select(r => r.Input));
    }

    [Fact]
    public async Task Retention_PrunesByAgeAndCountAndCascadesSamples()
    {
        var store = await StoreAsync();
        var now = DateTimeOffset.UtcNow;
        var ancient = Run(now - TimeSpan.FromDays(100), "ancient.mkv");
        await store.RecordAsync(ancient, [new(1, null, null, null, null, null, null, null, null)], new HistorySettings(RetentionDays: 365), Token);

        for (var i = 0; i < 5; i++)
        {
            await store.RecordAsync(Run(now - TimeSpan.FromMinutes(10 - i), $"run{i}.mkv"), [], new HistorySettings(RetentionDays: 30, MaxRuns: 3), Token);
        }

        Assert.Equal(["run4.mkv", "run3.mkv", "run2.mkv"], (await store.ListAsync(10, Token)).Select(r => r.Input));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM samples;"));
    }

    [Fact]
    public async Task Delete_AllOrOlderThan()
    {
        var store = await StoreAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RecordAsync(Run(now - TimeSpan.FromDays(10)), [], HistorySettings.Default, Token);
        await store.RecordAsync(Run(now), [], HistorySettings.Default, Token);

        Assert.Equal(1, await store.DeleteAsync(now - TimeSpan.FromDays(5), Token));
        Assert.Equal(1, await store.DeleteAsync(null, Token));
        Assert.Empty(await store.ListAsync(10, Token));
    }

    [Fact]
    public async Task NewerSchema_IsReadOnlyWithNotice()
    {
        await StoreAsync();
        await ScalarAsync("PRAGMA user_version = 99;");

        var (database, notice) = await HistoryDatabase.OpenAsync(DatabasePath, Token);

        Assert.True(database.ReadOnly);
        Assert.Contains("newer FluxEncoder", notice, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HistoryStore(database).RecordAsync(Run(DateTimeOffset.UtcNow), [], HistorySettings.Default, Token));
    }

    [Fact]
    public async Task CorruptFile_IsMovedAsideAndReplaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await File.WriteAllTextAsync(DatabasePath, new string('x', 8192), Token);

        var (database, notice) = await HistoryDatabase.OpenAsync(DatabasePath, Token);

        Assert.Contains("moved to", notice, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(DatabasePath)!, "history.db.corrupt-*"));
        await new HistoryStore(database).RecordAsync(Run(DateTimeOffset.UtcNow), [], HistorySettings.Default, Token);
    }

    [Fact]
    public async Task ConcurrentWriters_LoseNothing()
    {
        await StoreAsync();
        const int Writers = 8;
        const int RunsEach = 15;

        // Each writer has its own database handle and connections, as separate processes would.
        await Task.WhenAll(Enumerable.Range(0, Writers).Select(w => Task.Run(async () =>
        {
            var (database, _) = await HistoryDatabase.OpenAsync(DatabasePath, Token);
            var store = new HistoryStore(database);
            for (var i = 0; i < RunsEach; i++)
            {
                await store.RecordAsync(
                    Run(DateTimeOffset.UtcNow, $"w{w}-{i}.mkv"),
                    [new(i, null, null, null, null, null, null, null, null)],
                    new HistorySettings(MaxRuns: 100_000),
                    Token);
            }
        }, Token)));

        Assert.Equal((long)Writers * RunsEach, await ScalarAsync("SELECT COUNT(*) FROM runs;"));
        Assert.Equal((long)Writers * RunsEach, await ScalarAsync("SELECT COUNT(*) FROM samples;"));
        Assert.Equal("ok", await ScalarAsync("PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task ConcurrentProcesses_LoseNothing()
    {
        var sqlite3 = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, OperatingSystem.IsWindows() ? "sqlite3.exe" : "sqlite3"))
            .FirstOrDefault(File.Exists);
        Assert.SkipUnless(sqlite3 is not null, "The sqlite3 command-line tool is not on PATH.");
        await StoreAsync();

        // A second process writes while this one does; WAL and busy_timeout must serialize them without errors.
        using var other = Process.Start(new ProcessStartInfo(sqlite3!)
        {
            ArgumentList = { "-cmd", ".timeout 5000", DatabasePath },
            RedirectStandardInput = true,
            RedirectStandardError = true,
        })!;
        var writes = string.Join('\n', Enumerable.Range(0, 200).Select(i =>
            $"BEGIN IMMEDIATE; INSERT INTO runs (id, started_utc, finished_utc, input, output, outcome, exit_code, attempts, elapsed_seconds) VALUES ('ext-{i}', '2026-10-21T00:00:00.0000000Z', '2026-10-21T00:00:01.0000000Z', 'x', 'y', 'succeeded', 0, 1, 1.0); COMMIT;"));
        var feeding = other.StandardInput.WriteLineAsync(writes + "\n.quit");

        var store = new HistoryStore((await HistoryDatabase.OpenAsync(DatabasePath, Token)).Database);
        for (var i = 0; i < 50; i++)
        {
            await store.RecordAsync(Run(DateTimeOffset.UtcNow, $"own-{i}.mkv"), [], new HistorySettings(RetentionDays: 3650, MaxRuns: 100_000), Token);
        }

        await feeding;
        other.StandardInput.Close();
        await other.WaitForExitAsync(Token);

        Assert.Equal(string.Empty, (await other.StandardError.ReadToEndAsync(Token)).Trim());
        Assert.Equal(250L, await ScalarAsync("SELECT COUNT(*) FROM runs;"));
    }

    [Fact]
    public void Telemetry_StaysWithinCapacityAndSpreadsEvenly()
    {
        var recorder = new TelemetryRecorder(capacity: 100, interval: TimeSpan.FromSeconds(1));

        // Ten hours of reports, four per second.
        for (var tick = 0; tick < 10 * 3600 * 4; tick++)
        {
            recorder.Add(new EncodeProgress { ProcessedTime = TimeSpan.FromSeconds(tick / 4.0) }, TimeSpan.FromSeconds(tick / 4.0));
        }

        var samples = recorder.Samples;
        Assert.InRange(samples.Count, 50, 100);
        Assert.True(recorder.Interval >= TimeSpan.FromSeconds(256));
        Assert.True(samples[^1].ElapsedSeconds > 9 * 3600);
        Assert.Equal(samples.OrderBy(s => s.ElapsedSeconds), samples);
    }

    [Fact]
    public void Telemetry_SamplesAtMostOncePerIntervalButAlwaysKeepsFinal()
    {
        var recorder = new TelemetryRecorder(interval: TimeSpan.FromSeconds(1));

        recorder.Add(new EncodeProgress { Frame = 1 }, TimeSpan.FromSeconds(0.1));
        recorder.Add(new EncodeProgress { Frame = 2 }, TimeSpan.FromSeconds(0.5));
        recorder.Add(new EncodeProgress { Frame = 3 }, TimeSpan.FromSeconds(1.2));
        recorder.Add(new EncodeProgress { Frame = 4, IsFinal = true }, TimeSpan.FromSeconds(1.3));

        Assert.Equal([0.1, 1.2, 1.3], recorder.Samples.Select(s => s.ElapsedSeconds));
    }

    [Fact]
    public async Task Transaction_RollsBackOnFailure()
    {
        var (database, _) = await HistoryDatabase.OpenAsync(DatabasePath, Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.WriteAsync<int>(async (connection, transaction) =>
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO runs (id, started_utc, finished_utc, input, output, outcome, exit_code, attempts, elapsed_seconds) " +
                    "VALUES ('rolled-back', '2026-10-21T00:00:00.0000000Z', '2026-10-21T00:00:01.0000000Z', 'a.mkv', 'b.mkv', 'succeeded', 0, 1, 1.0);";
                await insert.ExecuteNonQueryAsync(Token);

                throw new InvalidOperationException("Simulated mid-transaction failure.");
            }, Token));

        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM runs WHERE id = 'rolled-back';"));
    }
}
