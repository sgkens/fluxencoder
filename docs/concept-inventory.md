# Concept source inventory

This inventory classifies every concept and legacy source file by role, so the files can be merged into `src/` deliberately instead of compiled as-is.

- **Tracker:** `TSKE001.4`
- **Date:** 2026-10-17
- **Scope:** the 29 `.cs` files originally in `FluxEncoderEngine/`, the design documents beside them, the legacy `CliApp/`, and `psm/`.
- **Location:** since `TSKE001.5`, concept files are in `archive/concept/` and the legacy CLI (including `FluxEncoderEngine.cs`) is in `archive/legacy-cli/`. Neither folder is compiled.

## Classification rules

| Class | Meaning | What happens to it |
|---|---|---|
| **Authoritative** | The latest revision of a concern. Its behavior is the specification for that concern. | Ported into the target project under the listed task, rewritten for the System.CommandLine and package decisions in ADR 0002. |
| **Merge input** | An older or parallel revision that still holds behavior the authoritative revision lacks. | The listed behavior is folded into the authoritative port; the rest is dropped. |
| **Superseded patch** | Fully replaced by a later revision. Nothing unique remains. | Not ported. Kept only in the archive for history. |
| **Design-only archive** | A design document, or a feature concept with no scheduled task or with a design the ADRs reject. | Reference material only. Ported only when a tracker task is added for it. |

None of the files compile as-is. Seven files subclass `Spectre.Console.Cli` commands, which ADR 0002 replaces with System.CommandLine; several depend on types that were never supplied (`MediaInfo`, `ProbeService`, `SystemMetrics`). "Authoritative" therefore means "the behavior to reproduce", not "the code to copy".

When a file mixes classes, the per-type rows decide. `TSKE001.5` uses these rows to remove duplicate definitions.

## Inventory

### Process execution and probing

| File | Rev. | Type(s) | Class | Target | Task |
|---|---|---|---|---|---|
| `fluxencoder_core_engine_v1_0.cs` | v1.0 | `FFmpegEngine`, `ProgressUpdate` | Authoritative | Infrastructure | `TSKE004.1`–`TSKE004.4` |
| | | `HardwareVendor`, `IHardwareStrategy`, `CpuStrategy`, `HardwareFactory` | Superseded patch | — | — |
| `fluxencoder_safety_logic.cs` | unversioned | `SafeProcessRunner` | Superseded patch | — | — |
| | | `BatchExpander` | Authoritative | Core (planning) / Infrastructure (file enumeration) | `TSKE007.6` |
| `FluxEncoderEngine.cs` | legacy | `Engine.GetSupportedCodecs`, `GetSupportedFormats`, `GetHardwareAccelerationInfo` | Merge input | Infrastructure | `TSKE003.3` |
| | | `Engine.GetMediaInfo` | Merge input | Infrastructure | `TSKE003.2` |
| | | `Engine.Initialize` (`where ffmpeg.exe`, install-path search) | Merge input | Infrastructure | `TSKE003.1` |
| | | `Engine.Transcode`, `RunProcess`, `CommandResult` | Superseded patch | — | — |

> **Note!** `FFmpegEngine` is the base for the runner because it adds progress parsing to the cancellation sequence that `SafeProcessRunner` already had. Both share the defects in `CODE_AUDIT.md` A-06, A-07, and A-15: stderr is only read after exit, the `CancelKeyPress` handler is removed with a new lambda that never matches the one added, and the reader task is not awaited. Port the sequence (send `q`, grace period, kill), not the code.

> **Note!** The legacy `Engine` quotes arguments into a single shell string and passes `Hashtable` results. Keep only its parsing of `ffmpeg -codecs`, `-formats`, and `-hwaccels` output, and of the FFprobe JSON. Its discovery is Windows-only (`where`, `.exe`) and is replaced by `TSKE003.1`.

### Hardware strategy and codec knowledge

| File | Rev. | Type(s) | Class | Target | Task |
|---|---|---|---|---|---|
| `fluxencoder_extended_engine.cs` | v2.0 | `HardwareStrategy` (vendor codec map, decode flags) | Authoritative | Core | `TSKE006.1` |
| | | `HardwareVendor` | Authoritative (single definition) | Core | `TSKE006.1` |
| | | `FilterGraphBuilder` | Authoritative | Core | `TSKE005.1` |
| | | `AudioProfile` | Authoritative | Core | `TSKE005.1`, `TSKE012.2` |
| `FluxEncoder.H266_Update.cs` | v14.1 | `CpuStrategy` (CPU quality presets, VVC mapping) | Merge input | Core | `TSKE006.4`, `TSKE006.6` |
| | | `MediaEncyclopediaPatch` | Merge input | Core | `TSKE006.6` |
| `fluxencoder_knowledge_base.cs` | v5.0 | `CodecDefinition`, `MediaEncyclopedia` | Authoritative | Core (data) / Cli (rendering) | none yet |

> **After Thought:** The concept has two incompatible hardware shapes: the v1.0 `IHardwareStrategy` interface (per-vendor classes with `ApplyFlags`) and the v2.0 `HardwareStrategy` class (one class switching on `Vendor`, with decode flags). `TSKE006.1` asks for a strategy registry, which matches the v1.0 interface shape better. The plan is to keep the v1.0 interface, fill it with v2.0's codec map and decode flags, and use the v14.1 `CpuStrategy` quality presets as the CPU implementation. The v14.1 patch also adds VVC by mutating `MediaEncyclopedia.Database` at run time; fold that entry into the static data instead.

### Command surface

| File | Rev. | Type(s) | Class | Target | Task |
|---|---|---|---|---|---|
| `fluxencoder_command_update.cs` | v14.0 | `EncodeSettings`, `EncodeCommand` | Authoritative | Cli | `TSKE007.1` |
| `fluxencoder_cli_interface_v1_0.cs` | v1.0 | `EncodeSettings.Validate`, multiple `-i`, `--extra-args` and its quote-aware splitter | Merge input | Cli / Core | `TSKE007.1`, `TSKE010.1` |
| `fluxencoder_smart_naming_engine.cs` | v14.0 | `OutputNameBuilder` | Authoritative | Core | `TSKE007.6` |
| `fluxencoder_advanced_commands.cs` | v4.0 | `MergeCommand`, `ThumbCommand` | Authoritative | Cli | `TSKE012.1`, `TSKE005.8` |
| `fluxencoder_advanced_engine.cs` | v4.0 | `ConcatService`, `TwoPassOrchestrator`, `ThumbnailBuilder` | Authoritative | Core (plans) / Infrastructure (temp files) | `TSKE005.6`–`TSKE005.8` |
| `fluxencoder_presentation_zenith.cs` | v10.0 | `ManPageHelpProvider`, `AboutCommand` | Design-only archive | — | `TSKE008.6` (version source only) |
| `fluxencoder_interactive_wizard.cs` | v11.0 | `BrowseCommand` | Design-only archive | — | none |
| `archive/legacy-cli/Program.cs` | legacy | `init`, `codecs`, `formats`, `hw-accel`, `media-info`, `transcode`, `--json` | Merge input | Cli | `TSKE007.1`, `TSKE007.5` |

> **Note!** v14.0 drops v1.0's `--extra-args` and input validation, but nothing indicates that the removal was intentional. Both return through the merge, with `--extra-args` documented as a trust boundary under `TSKE010.1`. Both revisions append `-y`, which contradicts the no-overwrite default in `TSKE007.1`; do not port it.

> **Note!** `ManPageHelpProvider` implements `Spectre.Console.Cli.Help.IHelpProvider`, which ADR 0002 rejects, and the About screen hard-codes `10.0.0 (NativeAOT)`. Keep the layout as design reference for a System.CommandLine help action. The interactive `browse` wizard has no tracker task; add one before porting it.

> **Note!** The legacy `CliApp` command names and the concept names differ (`transcode` vs `encode`, `media-info` vs none). `TSKE007.1` uses `encode`; the read-only `codecs`, `formats`, `hw-accel`, and `media-info` commands are candidates once `PRB-E003` is done.

### State, persistence, and telemetry

| File | Rev. | Type(s) | Class | Target | Task |
|---|---|---|---|---|---|
| `fluxencoder_unified_database.cs` | v8.0 | `FluxDatabase`, `FluxRunRecord`, `TelemetryDatapoint` | Authoritative | Infrastructure | `TSKE009.2`–`TSKE009.7` |
| `fluxencoder_state_management.cs` | v5.0 | `HistoryTracker`, `FluxHistoryRecord` | Superseded patch | — | — |
| | | `PresetManager`, `EncodingPreset` | Merge input | Core (model) / Infrastructure (storage) | `TSKE009.6` |
| `fluxencoder_config_manager.cs` | v6.0 | `FluxConfig`, `ConfigManager` | Authoritative | Infrastructure | `TSKE009.1`, `TSKE009.6` |
| `fluxencoder_refactored_telemetry.cs` | v8.0 | `ProcessMonitor` | Authoritative | Infrastructure | `TSKE009.5`, `TSKE009.8` |

> **After Thought:** The v8.0 database header says it "handles History, Presets", but it creates only `ExecutionHistory` and `Telemetry` tables. Presets therefore survive from v5.0 as merge input until `TSKE009.6` decides between a presets table and an atomic JSON file. The three files also disagree on location: `$HOME/.fluxencoder/` (config, database) versus the platform `ApplicationData` folder (presets, history). `TSKE009.1` settles one location.

### Network, remote sources, and manifests

| File | Rev. | Type(s) | Class | Target | Task |
|---|---|---|---|---|---|
| `fluxencoder_remote_vault.cs` | v9.0 | `RemoteMount`, `VaultManager` | Authoritative (behavior); storage design rejected | Infrastructure | `TSKE010.3` |
| `fluxencoder_uri_translator.cs` | v9.0 | `ResolvedUri`, `UriTranslator` | Authoritative | Core | `TSKE010.4` |
| `fluxencoder_declarative_engine.cs` | v12.0 | `FluxfileManifest`, `FluxJob`, `VideoSpec`, `AudioSpec`, `FluxfileParser` | Design-only archive | — | `TSKE011.1` |

> **Note!** `VaultManager` stores passwords in plain SQLite (`CODE_AUDIT.md` A-08); only the alias model and lookup flow carry forward. `FluxfileParser` returns an empty manifest regardless of input (A-09), so only the model shape is reference for the schema in `TSKE011.1`.

### Quality assurance and exports

| File | Rev. | Type(s) | Class | Target | Task |
|---|---|---|---|---|---|
| `fluxencoder_qa_engine.cs` | v13.0 | `AuditEvent`, `AuditReport`, `AuditEngine` | Authoritative | Core (model) / Infrastructure (FFmpeg pass) | `TSKE012.5`, `TSKE012.6` |
| `fluxencoder_audit_ui.cs` | v13.0 | `AuditCommand` | Authoritative | Cli | `TSKE012.5` |
| `fluxencoder_multi_format_exporters.cs` | v6.0 | `MetadataExporter`, `SvgBadgeGenerator` | Authoritative | Core | `TSKE011.5` |
| `fluxencoder_analytics_ui.cs` | v7.0 | `CompareCommand`, `TuiTimeSeriesChart` | Authoritative | Cli | `TSKE009.8` |

### Terminal rendering

| File | Rev. | Type(s) | Class | Target | Task |
|---|---|---|---|---|---|
| `fluxencoder_foundation_gradients_padding.cs` | v5.0 | `TuiPalette`, `TuiBorders`, `TuiPadder` | Authoritative | Cli | `TSKE008.2`, `TSKE008.3` |
| `fluxencoder_nextgen_ui_gauges.cs` | v3.0 | `TuiGauge`, `OperationsDashboard` | Authoritative (finite-job view) | Cli | `TSKE008.4` |
| `fluxencoder_streaming_ui.cs` | v5.0 | `StreamingDashboard` | Authoritative (live-stream view) | Cli | `TSKE008.4`, `TSKE012.4` |
| `fluxencoder_visual_logic.cs` | unversioned | `AsciiSparkline`, `PostActionReport` | Authoritative (components) | Cli | `TSKE008.4` |
| `fluxencoder_ui_implementation.cs` | unversioned | `DashboardRenderer`, `ProgressBar` | Superseded patch | — | — |

> **Note!** `TuiPalette`/`TuiBorders` already follow the centralised palette and border pattern, and `TuiPadder` pads before applying colour. `TuiPadder` still measures with `string.Length` and the dashboards assume fixed widths, which `TSKE008.2` and `TSKE008.3` replace. `OperationsDashboard` needs `SystemMetrics`, which was never supplied; its fields map to `ProcessMonitor` samples.

### Documents and other sources

| Path | Class | Notes |
|---|---|---|
| `archive/concept/fluxencoder_product_design_reference_v9.md` | Design-only archive | Latest product design reference. |
| `archive/concept/fluxencoder_product_design_reference_v7.md`, `_v8.md` | Superseded patch | Earlier revisions of v9. |
| `archive/concept/fluxencoder_technical_documentation.md` | Design-only archive | Build claims (NativeAOT, ~15 MB binary) are unverified. |
| `archive/concept/ui_design_analysis.md`, `fluxencoder_tui_preview.html` | Design-only archive | Visual reference for `UI-E008`. |
| `archive/concept/competitive_analysis.md`, `industry_standard_roadmap.md` | Design-only archive | Product positioning; not a specification. |
| `archive/legacy-cli/FluxEncoderCli.csproj` | Superseded patch | Replaced by `src/FluxEncoder.Cli`; framework and RID settings rejected by ADR 0001. |
| `psm/FluxEncoder.psm1` | Placeholder | Empty. A wrapper that invokes the published executable needs its own tracker task. |

## Summary

| Class | `.cs` files (whole or in part) |
|---|---|
| Authoritative | 21 |
| Merge input | 5 (`FluxEncoder.H266_Update.cs`, `fluxencoder_cli_interface_v1_0.cs`, `fluxencoder_state_management.cs`, `FluxEncoderEngine.cs`, legacy `Program.cs`) |
| Superseded patch | 5 (`fluxencoder_ui_implementation.cs` whole; parts of `fluxencoder_core_engine_v1_0.cs`, `fluxencoder_safety_logic.cs`, `fluxencoder_state_management.cs`, `FluxEncoderEngine.cs`) |
| Design-only archive | 3 (`fluxencoder_presentation_zenith.cs`, `fluxencoder_interactive_wizard.cs`, `fluxencoder_declarative_engine.cs`) |

Mixed files are counted once in each class they contribute to.
