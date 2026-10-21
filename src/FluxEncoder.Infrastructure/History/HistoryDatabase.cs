using System.Globalization;
using FluxEncoder.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace FluxEncoder.Infrastructure.History;

/// <summary>
/// The single connection factory for <c>history.db</c> (TSKE009.2, TSKE009.3). Every connection it hands out is
/// configured the same way, and every write goes through <see cref="WriteAsync{T}"/> with an explicit transaction.
/// </summary>
/// <remarks>
/// <para>
/// Per connection: <c>foreign_keys = ON</c> (SQLite defaults to off), <c>busy_timeout = 5000</c> so a second process
/// waits for a lock instead of failing, and <c>synchronous = NORMAL</c>, which is durable across application crashes
/// in WAL mode. The database uses WAL journaling, set once at creation, so readers never block the writer.
/// </para>
/// <para>
/// Writes start with <c>BEGIN IMMEDIATE</c>, taking the write lock up front. A deferred transaction that reads and
/// then writes can fail with SQLITE_BUSY when another process writes in between; immediate transactions queue
/// instead, which is what lets several FluxEncoder processes record history concurrently (TSKE009.4).
/// </para>
/// <para>
/// Recovery: a file SQLite reports as corrupt or not a database is moved aside to
/// <c>history.db.corrupt-&lt;UTC time&gt;</c> (with its <c>-wal</c> and <c>-shm</c> files) and a fresh database is
/// created; the caller shows the returned notice. Keep WAL databases on local disks: SQLite's locking is not reliable
/// on network file systems.
/// </para>
/// </remarks>
public sealed class HistoryDatabase
{
    private const int BusyTimeoutMilliseconds = 5000;
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    private readonly string _connectionString;

    private HistoryDatabase(string path, bool readOnly)
    {
        Path = path;
        ReadOnly = readOnly;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000,
        }.ToString();
    }

    public string Path { get; }

    /// <summary>
    /// The database was written by a newer FluxEncoder; it is read but never written.
    /// </summary>
    public bool ReadOnly { get; }

    /// <summary>
    /// Opens (creating and migrating when needed) the database at <paramref name="path"/>.
    /// </summary>
    /// <returns>The database and any notice for the user (newer schema, recovered corruption).</returns>
    public static async Task<(HistoryDatabase Database, string? Notice)> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AppPaths.EnsureDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);

        try
        {
            return await OpenAndMigrateAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            var moved = MoveAside(path);
            var (database, notice) = await OpenAndMigrateAsync(path, cancellationToken).ConfigureAwait(false);
            return (database, $"The history database was damaged ({ex.Message.TrimEnd('.')}) and was moved to '{moved}'; a new one was started.{(notice is null ? string.Empty : " " + notice)}");
        }
    }

    /// <summary>
    /// A configured, open connection. Callers dispose it.
    /// </summary>
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var pragmas = connection.CreateCommand();
        pragmas.CommandText = string.Create(CultureInfo.InvariantCulture,
            $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {BusyTimeoutMilliseconds}; PRAGMA synchronous = NORMAL;");
        await pragmas.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }

    /// <summary>
    /// Runs <paramref name="work"/> in an immediate transaction and commits it. Every command <paramref name="work"/>
    /// creates must set <see cref="SqliteCommand.Transaction"/> to the transaction it receives.
    /// </summary>
    /// <exception cref="InvalidOperationException">The database is read-only (newer schema).</exception>
    public async Task<T> WriteAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (ReadOnly)
        {
            throw new InvalidOperationException("The history database was written by a newer FluxEncoder and is read-only.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);

        var result = await work(connection, transaction).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task<(HistoryDatabase, string?)> OpenAndMigrateAsync(string path, CancellationToken cancellationToken)
    {
        var database = new HistoryDatabase(path, readOnly: false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Journal mode cannot change inside a transaction; it is persistent, so setting it on every open is cheap.
        await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);

        // deferred: false issues BEGIN IMMEDIATE, so two processes opening a new database migrate one after the other.
        await using var transaction = connection.BeginTransaction(deferred: false);
        var version = Convert.ToInt32(await ScalarAsync(connection, transaction, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

        if (version > HistorySchema.LatestVersion)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return (new HistoryDatabase(path, readOnly: true), $"The history database was created by a newer FluxEncoder (schema {version}); runs are not recorded until you upgrade.");
        }

        for (var next = version; next < HistorySchema.LatestVersion; next++)
        {
            await ExecuteAsync(connection, transaction, HistorySchema.Migrations[next], cancellationToken).ConfigureAwait(false);
        }

        if (version != HistorySchema.LatestVersion)
        {
            await ExecuteAsync(connection, transaction, string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {HistorySchema.LatestVersion};"), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return (database, null);
    }

    private static string MoveAside(string path)
    {
        SqliteConnection.ClearAllPools();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var target = $"{path}.corrupt-{stamp}";
        File.Move(path, target);

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                File.Move(path + suffix, target + suffix);
            }
        }

        return target;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }
}
