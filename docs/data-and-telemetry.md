# Data, history, and telemetry

FluxEncoder keeps a settings file, a local SQLite history of runs with progress samples, and nothing else; this document says where they live, what each stored value means, and how to inspect, limit, or delete them.

## Locations

Run `config path` to print the paths in use.

| File | Linux (XDG) | macOS | Windows |
|---|---|---|---|
| `settings.json` | `~/.config/fluxencoder/` | `~/Library/Application Support/FluxEncoder/` | `%APPDATA%\FluxEncoder\` |
| `history.db` | `~/.local/share/fluxencoder/` | `~/Library/Application Support/FluxEncoder/` | `%LOCALAPPDATA%\FluxEncoder\` |
| cache | `~/.cache/fluxencoder/` | `~/Library/Caches/FluxEncoder/` | `%LOCALAPPDATA%\FluxEncoder\Cache\` |

- `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, and `XDG_CACHE_HOME` are honoured when they hold absolute paths.
- `FLUXENCODER_HOME` replaces all three with `config/`, `data/`, and `cache/` under one folder, for portable installs.
- On Unix, directories are created `0700` and files `0600`. The history database is never placed in the roaming Windows profile, because SQLite files must not be synchronised while open.

### Ownership

- `settings.json` belongs to the user. FluxEncoder writes it only through `config init`, atomically, keeping the previous version as `settings.json.bak`. A broken file is reported and left untouched; built-in defaults apply until it is fixed.
- `history.db` and the cache belong to FluxEncoder and can be deleted at any time.

## Settings

`settings.json` holds encode defaults, named presets, and history limits. Values are written exactly as on the command line, and typed flags always win over a preset, which wins over the `encode` defaults.

```jsonc
// ~/.config/fluxencoder/settings.json
{
  "schemaVersion": 1,
  "encode": { "codec": "hevc", "quality": "balanced" },
  "presets": {
    "web": { "description": "MP4 for browsers", "codec": "h264", "audio-codec": "aac", "format": "mp4" }
  },
  "history": { "enabled": true, "telemetry": true, "retentionDays": 90, "maxRuns": 1000 }
}
```

`output`, `dry-run`, `json`, `preset`, and `no-config` cannot be preset. `encode --no-config` ignores the `encode` defaults for one run.

## History

Each finished `encode` (not `--dry-run`) adds one row to `runs`, and its samples to `samples`, in a single transaction. Recording problems are reported once and never fail an encode.

### Identifiers and timestamps

- `runs.id` is a UUID version 7: time-ordered, and unique across processes and machines without coordination.
- Timestamps are UTC ISO 8601 with seven fractional digits, `2026-10-21T09:05:03.1234567Z`, which sort correctly as text.

### Retention and growth

- Runs older than `history.retentionDays` (default 90) and the oldest runs beyond `history.maxRuns` (default 1000) are deleted whenever a run is recorded. Samples are deleted with their run.
- Per run, at most 1,800 samples are kept. Sampling starts at one per second; each time the limit is reached, every other sample is dropped and the interval doubles, so a ten-hour recording takes the same space as a thirty-minute encode, at lower resolution.
- `history.telemetry: false` keeps runs but no samples; `history.enabled: false` records nothing.

### Concurrent use

Several FluxEncoder processes can record at once. The database runs in WAL mode, every connection waits up to five seconds for a lock, and every write takes the write lock at the start of its transaction (`BEGIN IMMEDIATE`). Runs are only ever inserted under unique identifiers, so no process can overwrite another's record. Keep `history.db` on a local disk: SQLite's locking is unreliable on network file systems.

### Schema versions and recovery

- `PRAGMA user_version` records the schema version. Newer FluxEncoder versions migrate older databases in place, inside one transaction.
- A database written by a newer FluxEncoder is opened read-only and runs are not recorded until FluxEncoder is upgraded.
- A file SQLite reports as corrupt is renamed to `history.db.corrupt-<UTC time>` (with its `-wal` and `-shm` files) and a new database is started. The renamed file can be examined or salvaged with the `sqlite3` tool and then deleted.

```bash
# bash: check a database by hand
sqlite3 ~/.local/share/fluxencoder/history.db 'PRAGMA integrity_check;'
```

## Telemetry definitions

Every metric is measured on this machine and stored only in `history.db`. A missing value is stored as `NULL` and shown as unavailable; it is never recorded as zero.

| Column | Meaning | Source |
|---|---|---|
| `elapsed_seconds` | Wall-clock seconds since the run started | FluxEncoder clock |
| `processed_seconds` | Media time processed so far (output timestamp) | FFmpeg `out_time_us` |
| `fps` | Frames encoded per wall-clock second | FFmpeg `fps` |
| `speed` | Processing speed relative to playback; 2.0 is twice real time | FFmpeg `speed` |
| `bitrate_kbps` | Average output bit rate so far, kbit/s | FFmpeg `bitrate` |
| `size_bytes` | Output bytes written so far | FFmpeg `total_size` |
| `cpu_percent` | FFmpeg's CPU time between samples divided by wall time and the logical core count, as a share of the whole machine, 0–100 | Operating system process counters |
| `memory_bytes` | FFmpeg's resident memory (working set) | Operating system process counters |
| `dropped_frames` | Frames FFmpeg dropped to hold the output frame rate | FFmpeg `drop_frames` |

### Not measured

- **GPU utilisation and GPU memory** are not recorded. There is no portable way to read them for one process across NVIDIA, AMD, Intel, and Apple drivers, so FluxEncoder does not report a figure it cannot measure.
- **CPU use of other processes** (including FluxEncoder itself) is excluded; `cpu_percent` is FFmpeg's alone.
- The first sample of a run has no `cpu_percent`, because CPU share needs two readings.

## Deleting data

```bash
# bash, from any directory
FluxEncoder.Cli history clear                    # delete every recorded run
FluxEncoder.Cli history clear --older-than 30    # delete runs older than 30 days
rm -rf ~/.local/share/fluxencoder ~/.cache/fluxencoder   # remove history and cache entirely (Linux)
```
