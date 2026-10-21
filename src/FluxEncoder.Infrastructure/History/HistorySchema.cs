namespace FluxEncoder.Infrastructure.History;

/// <summary>
/// The history database schema as ordered migrations. The database's <c>PRAGMA user_version</c> is the number of
/// migrations applied; a migration is never edited once released, only followed by a new one.
/// </summary>
/// <remarks>
/// <para>Formats (TSKE009.7):</para>
/// <list type="bullet">
/// <item><c>runs.id</c>: UUID version 7, lower-case hyphenated (<c>0192f1c4-…</c>). Time-ordered and collision-resistant
/// across processes and machines, unlike the concept's eight-character IDs.</item>
/// <item>Timestamps: UTC ISO 8601 with seven fractional digits and a literal <c>Z</c>
/// (<c>2026-10-21T20:41:03.1234567Z</c>), which sorts correctly as text.</item>
/// <item><c>samples</c> metric units are in the column names; NULL means not measured (see <c>docs/data-and-telemetry.md</c>).</item>
/// </list>
/// <para>Tables are STRICT, so a wrong value type is an error rather than a silent coercion.</para>
/// </remarks>
public static class HistorySchema
{
    public static IReadOnlyList<string> Migrations { get; } =
    [
        """
        CREATE TABLE runs (
            id               TEXT    PRIMARY KEY,
            started_utc      TEXT    NOT NULL,
            finished_utc     TEXT    NOT NULL,
            input            TEXT    NOT NULL,
            output           TEXT    NOT NULL,
            outcome          TEXT    NOT NULL CHECK (outcome IN ('succeeded', 'skipped', 'failed')),
            exit_code        INTEGER NOT NULL,
            failure_category TEXT,
            failure_message  TEXT,
            video_encoder    TEXT,
            hardware         TEXT,
            attempts         INTEGER NOT NULL,
            elapsed_seconds  REAL    NOT NULL,
            input_bytes      INTEGER,
            output_bytes     INTEGER,
            command          TEXT,
            warnings         TEXT    NOT NULL DEFAULT '[]'
        ) STRICT;

        CREATE INDEX runs_started ON runs (started_utc);

        CREATE TABLE samples (
            run_id            TEXT    NOT NULL REFERENCES runs (id) ON DELETE CASCADE,
            elapsed_seconds   REAL    NOT NULL,
            processed_seconds REAL,
            fps               REAL,
            speed             REAL,
            bitrate_kbps      REAL,
            size_bytes        INTEGER,
            cpu_percent       REAL,
            memory_bytes      INTEGER,
            dropped_frames    INTEGER,
            PRIMARY KEY (run_id, elapsed_seconds)
        ) STRICT, WITHOUT ROWID;
        """,
    ];

    public static int LatestVersion => Migrations.Count;
}
