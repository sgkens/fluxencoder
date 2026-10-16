# FluxEncoder

FluxEncoder is a planned command-line workspace for converting, inspecting, and quality-checking media with FFmpeg. Its goal is to make professional media workflows easier to understand: choose an input, select the desired result, watch the job progress, and receive a useful completion report.

The project is designed for both guided interactive use and repeatable automation. FFmpeg performs the media processing; FluxEncoder is intended to provide the safer commands, presets, progress display, history, and reporting around it.

> [!IMPORTANT]
> This repository is currently an integration snapshot, not a production release. The complete set of concept source files has been copied into the repository, but those files have not yet been merged into the build. The checked-in CLI and the copied concept both have compilation blockers. See [Project status](#project-status) and [CODE_AUDIT.md](CODE_AUDIT.md) before attempting to package or distribute it.

## Why FluxEncoder?

FFmpeg is powerful, but its command line can be difficult to learn and its live output can be hard to follow. FluxEncoder aims to add a clearer operating layer without hiding FFmpeg's capabilities.

The intended experience includes:

- straightforward encoding for common formats;
- automatic selection of supported CPU or GPU encoders;
- a guided terminal interface for people who do not want to construct long commands;
- readable live progress, speed, bitrate, CPU, and memory information;
- batch processing and reusable presets;
- media inspection and quality checks;
- saved job history and performance comparisons;
- an advanced pass-through option for experienced FFmpeg users.

## Project status

| Area | Current state |
|---|---|
| Concept source inventory | Complete: 27 of 27 source `.cs` files from the available concept source were copied byte-for-byte |
| Existing .NET 8 CLI | Present, but does not compile because of a syntax error |
| Copied next-generation engine | Present, but not referenced by the solution and does not compile as one unit |
| .NET SDKs | .NET 8 and .NET 10 are available in the local `phellams-automator:localbuild` image |
| FFmpeg integration | Prototype implementations exist; end-to-end integration is incomplete |
| Automated tests | Not present |
| Release packages | Not present |
| Production readiness | Blocked pending consolidation, tests, security work, and packaging |

The copied files represent several stages of the design, from v1 through v14. They include replacement classes and update patches that overlap with earlier files. They must be consolidated into a single source of truth rather than compiled together unchanged.

## How the finished product is intended to work

### 1. Choose media

Users will be able to provide one file, several files, a directory, or a supported batch pattern. A guided browser is also planned for interactive terminals.

### 2. Inspect the source

FluxEncoder will ask FFprobe to identify the container, video and audio streams, dimensions, duration, bitrate, and other useful details. This allows the application to explain what it found before any processing begins.

### 3. Choose the result

A user will select a video format, audio format, quality level, output container, and optional hardware target. Sensible presets are intended to cover common jobs while an advanced argument option preserves access to FFmpeg features.

### 4. Confirm the plan

Before starting, the terminal interface is intended to show a plain-language comparison between the source and target: what will be copied, converted, resized, or extracted.

### 5. Monitor the job

During processing, the planned dashboard shows progress and live performance information without filling the terminal with raw log output. Non-interactive environments should receive stable text or structured output instead.

### 6. Review the result

After a successful job, FluxEncoder is intended to report elapsed time, average speed, original and final sizes, and space saved. Optional history and telemetry make it possible to compare past runs.

## Planned capabilities

These capabilities are represented in the concept files, but should not yet be treated as released features.

### Encoding and media operations

- CPU encoding with H.264, HEVC, AV1, and experimental VVC mappings.
- NVIDIA NVENC, AMD AMF, Intel Quick Sync, Apple VideoToolbox, and software strategies.
- Stream copy when re-encoding is unnecessary.
- Multiple inputs and advanced FFmpeg arguments.
- Video scaling, cropping, frame-rate changes, and audio normalization.
- Two-pass encoding.
- Lossless concatenation where source streams are compatible.
- Thumbnail or contact-sheet generation.
- Smart output names based on codec, hardware, resolution, or quality.

Actual codec availability always depends on the installed FFmpeg build and the host hardware. A listed codec is not a guarantee that every machine can use it.

### Guided terminal experience

- Interactive file selection.
- Hardware, codec, quality, audio, and container prompts.
- Live gauges and bitrate history.
- A stable dashboard intended to adapt to terminal width.
- Plain text output for logs, build agents, and other non-interactive sessions.

### Inspection, reports, and quality checks

- Media and stream inspection through FFprobe.
- Export ideas for JSON, YAML, Markdown, HTML, and SVG badges.
- Silence and black-frame detection.
- Stored execution history and performance telemetry.
- Side-by-side comparisons of previous runs.

### Repeatable automation

- Saved presets.
- A planned `Fluxfile.yaml` format for version-controlled encoding jobs.
- Batch input expansion.
- Remote-path aliases such as `remote://archive/path/video.mkv`.

The YAML loader and secure credential storage are currently placeholders. They must be completed before these automation features can be enabled safely.

## Intended command experience

The final command tree still needs to be consolidated and registered. The concept source describes the following direction:

```text
flux encode     Convert one or more media files
flux browse     Configure a job through guided prompts
flux merge      Join compatible media files
flux thumb      Create a thumbnail sheet
flux audit      Scan for silence and black frames
flux compare    Compare performance from two saved runs
flux apply      Run jobs described in a Fluxfile
flux about      Show version and environment information
```

Example syntax shown in the concept design:

```bash
flux encode \
  --input input.mkv \
  --output output.mp4 \
  --hardware nvidia \
  --codec-video hevc \
  --codec-audio aac \
  --quality balanced
```

For an advanced FFmpeg option:

```bash
flux encode \
  --input input.avi \
  --output output.mp4 \
  --codec-video h264 \
  --extra-args "-movflags +faststart"
```

These examples document the target interface. They will not work from the current snapshot until the integration work in the audit is complete.

## Requirements

### Planned runtime requirements

- A supported 64-bit Windows, Linux, or macOS system.
- FFmpeg and FFprobe available on `PATH`, including the codecs required by the chosen job.
- A terminal with UTF-8 support for the full dashboard experience.

### Development requirements

- .NET 10 SDK (10.0.100 or a later 10.0 feature band, pinned by `global.json`).
- FFmpeg and FFprobe for integration testing.
- Docker, optionally, for the local build environment.

FluxEncoder targets `net10.0` only. The decision and the conditions for revisiting it are recorded in [docs/adr/0001-target-framework-policy.md](docs/adr/0001-target-framework-policy.md). The legacy .NET 8 prototype is archived in `archive/legacy-cli/` and is not built.

## Building from source

The solution builds the project skeleton only; no commands are implemented yet. Run from the repository root:

```bash
dotnet restore fluxencoder.sln
dotnet build fluxencoder.sln --configuration Release --no-restore
dotnet test --solution fluxencoder.sln --configuration Release --no-build
```

Using the local build image:

```bash
docker run --rm \
  --volume "$PWD:/workspace" \
  --workdir /workspace \
  phellams-automator:localbuild \
  dotnet build fluxencoder.sln --configuration Release
```

The local image was verified to contain .NET SDK 8.0.424 and 10.0.400. The image is currently identified by a local tag, so published build instructions should replace it with a stable registry name and immutable version or digest.

## Repository guide

```text
.
├── src/
│   ├── FluxEncoder.Core/            Domain models, planning, contracts (no I/O dependencies)
│   ├── FluxEncoder.Infrastructure/  FFmpeg/FFprobe processes, file system, persistence
│   └── FluxEncoder.Cli/             Composition root and command surface
├── tests/                  One xUnit project per src project
├── docs/adr/               Architecture decision records
├── docs/concept-inventory.md  Classification of every concept and legacy source file
├── archive/
│   ├── concept/            Concept modules and design references (not compiled)
│   └── legacy-cli/         Legacy .NET 8 prototype CLI (not compiled)
├── psm/                    PowerShell wrapper module
├── Directory.Build.props   Shared target framework, compiler, and restore settings
├── Directory.Packages.props  Central package versions
├── global.json             .NET SDK pin
├── fluxencoder.sln         Solution containing src/ and tests/ projects
├── README.md               Product and contributor overview
├── CODE_AUDIT.md           Verified integration and production-readiness findings
└── devtracker-pending.md   Dependency-ordered implementation and test backlog
```

The `archive/` folder is reference material for porting, not a project. [docs/concept-inventory.md](docs/concept-inventory.md) lists what each file becomes.

## Architecture direction

The concept naturally separates into the following areas:

| Area | Responsibility |
|---|---|
| Command layer | Parses commands, validates choices, and starts workflows |
| Media engine | Builds safe FFmpeg argument lists and manages the child process |
| Probe service | Reads media metadata through FFprobe |
| Hardware strategy | Chooses an encoder and settings supported by the host |
| Terminal UI | Shows prompts, progress, dashboards, and completion reports |
| State and data | Stores configuration, presets, history, and telemetry |
| Quality assurance | Scans media for silence, black frames, and other issues |
| Export layer | Produces machine-readable and presentation-friendly reports |
| Remote access | Resolves saved remote locations without exposing secrets |

Production integration should keep these boundaries, but give every type one authoritative implementation and place each area in a real project with explicit dependencies.

## Configuration and local data

The concepts currently use a `.fluxencoder` directory in the user's profile and, in older files, the platform application-data directory. The final implementation should choose one platform-aware location and document it consistently.

Expected data includes:

- user preferences;
- saved presets;
- job history;
- performance telemetry;
- optional remote connection profiles.

Before release, FluxEncoder must provide clear retention and deletion controls. Passwords or access tokens must use the operating system's secure credential store and must never be written as plain text to SQLite, configuration files, command history, logs, or generated reports.

## Safety principles

Media processing can overwrite files or leave partial output when interrupted. The production implementation should guarantee the following:

- arguments are passed as separate process arguments rather than assembled into a shell command;
- an existing output is never overwritten without an explicit option or confirmation;
- cancellation first asks FFmpeg to finish and close the output cleanly;
- a forced stop is used only after a documented timeout;
- input and output paths are validated before work starts;
- secrets are redacted from every user-visible message and stored record;
- temporary files and two-pass logs are removed even after failure or cancellation;
- batch jobs isolate failures and report which files succeeded or failed.

## Troubleshooting

### FFmpeg or FFprobe is not found

Install FFmpeg, then confirm both commands are available:

```bash
ffmpeg -version
ffprobe -version
```

If either command fails, add the directory containing the executable to `PATH` and open a new terminal.

### A hardware encoder is unavailable

Hardware encoding depends on the GPU, its driver, and how FFmpeg was built. Check the encoders exposed by the local installation:

```bash
ffmpeg -hide_banner -encoders
```

Use CPU encoding when the requested hardware encoder is absent.

### The current source does not build

This is a known state of the integration snapshot. The existing CLI contains a malformed expression, while the concept modules contain overlapping updates and missing shared models. The full diagnosis and recommended repair order are recorded in [CODE_AUDIT.md](CODE_AUDIT.md).

## Roadmap to the first production release

1. Select the authoritative command model and remove superseded patches.
2. Create real Core, Infrastructure, CLI, and test projects targeting the chosen framework.
3. Implement the missing probe, media, metrics, and analytics models.
4. Consolidate FFmpeg execution into one deadlock-safe, cancellable process runner.
5. Complete hardware detection and verify encoder settings on supported platforms.
6. Replace YAML and credential-storage placeholders with production implementations.
7. Add unit, integration, cancellation, failure, and cross-platform tests.
8. Add continuous integration for .NET 10 and each supported operating system.
9. Publish signed, versioned artifacts with checksums and a software bill of materials.
10. Promote commands from “planned” to “supported” only after end-to-end verification.

## Documentation guidance

This README is intentionally organized so its sections can become pages in a documentation site:

- **Introduction:** Why FluxEncoder?
- **User guide:** How the finished product is intended to work
- **Features:** Planned capabilities
- **CLI guide:** Intended command experience
- **Getting started:** Requirements and Building from source
- **Concepts:** Architecture direction
- **Operations:** Configuration, local data, safety, and troubleshooting
- **Contributing:** Roadmap to the first production release

When the application becomes runnable, split command-specific examples into dedicated pages generated from the real command definitions. This avoids the website and CLI help drifting apart.

## Contributing

Until the integration baseline is established, changes should focus on one area at a time and include tests. Avoid adding another versioned replacement file; update the authoritative implementation instead.

A contribution should normally include:

- a short explanation of the user-facing change;
- tests for success, invalid input, cancellation, and FFmpeg failure where relevant;
- updated CLI help and documentation;
- confirmation that secrets and local paths are not exposed in logs;
- successful Release builds for the supported target frameworks and platforms.

## License

No license file is currently present. Until a license is added, normal copyright restrictions apply and the project should not be presented as open source or redistributed as though a license had been granted.

## Further technical detail

See [CODE_AUDIT.md](CODE_AUDIT.md) for the source inventory, compiler evidence, security findings, and recommended integration sequence.

See [devtracker-pending.md](devtracker-pending.md) for the actionable blocker fixes, architecture work, logic-gap backlog, dependencies, and testing-readiness milestones.
