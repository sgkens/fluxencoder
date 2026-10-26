# ADR 0003: Release artifact policy

FluxEncoder ships one NativeAOT executable named `flux` per operating system and architecture, plus one framework-dependent portable build as a fallback; self-contained and single-file JIT builds are not published.

- **Status:** Accepted
- **Date:** 2026-10-26
- **Tracker:** `TSKE015.1`

## Context

`fluxb.ps1 Publish` could already produce four layouts (framework-dependent per RID, self-contained, single-file, NativeAOT), but none was designated as the release artifact, and nothing guaranteed the code stayed AOT-compatible between releases. The concept documents assume a single native binary, and ADR 0002 chose System.CommandLine partly because it runs under NativeAOT.

A trial NativeAOT publish for `linux-x64` on 2026-10-26 (SDK 10.0, Debian 12) found:

- two trim/AOT warnings, both from the generic `JsonArray.Add<T>` in `MediaReport.ToJson`; with those fixed the publish is warning-free with warnings treated as errors;
- Spectre.Console 0.57.2, System.CommandLine 2.0.12, Microsoft.Data.Sqlite 10.0.12, and YamlDotNet 18.1.0 (FluxEncoder uses only its event parser and emitter, not reflection-based object mapping) produce no warnings;
- the runtime behaves the same as the JIT build for `info` in all six formats, `encode` (human and `--json`), `--dry-run`, `merge`, `history` (SQLite), `config init`/`show`, `apply` with a YAML Fluxfile, and SIGINT cancellation (exit 130, partial output deleted, no orphaned FFmpeg);
- the executable is 8.8 MB plus `libe_sqlite3.so` (1.5 MB), against roughly 70 MB for a self-contained JIT build;
- both the executable and `libe_sqlite3.so` require glibc 2.34 or later.

## Decision

1. **Executable name.** The CLI assembly is named `flux` (`flux.exe` on Windows). The project, namespaces, and test projects keep the `FluxEncoder.Cli` name.
2. **Release artifact.** Each supported RID gets a NativeAOT build: the `flux` executable, the SQLite native library beside it (`libe_sqlite3.so`, `e_sqlite3.dll`, or `libe_sqlite3.dylib`), and the licence files. Debug symbols (`.dbg`, `.pdb`, `.dSYM`) go into a separate symbols archive, not the main archive.
3. **Fallback artifact.** One RID-neutral framework-dependent build (`fluxb.ps1 Publish -Portable`) for platforms without a native build. It needs the .NET 10 runtime and runs with `dotnet flux.dll`.
4. **Not published.** Self-contained and single-file JIT builds stay available locally through `fluxb.ps1`, but they are not release artifacts: they are eight times larger than the AOT build and offer nothing the AOT build lacks.
5. **AOT compatibility is enforced on every build.** `FluxEncoder.Core` and `FluxEncoder.Infrastructure` set `IsAotCompatible`, and `FluxEncoder.Cli` sets `PublishAot`. Both enable the trim and AOT analyzers during a normal `dotnet build`, so with `TreatWarningsAsErrors` an AOT-incompatible API fails the build, not the release.
6. **Locked restore covers every RID.** `src/Directory.Build.props` declares the release RIDs in `RuntimeIdentifiers`, so `packages.lock.json` records each one. Publishing restores with the project's own properties and then runs `dotnet publish --no-restore`: passing `-r`, `PublishAot`, or `Version` to a restoring publish changes what the lock file records, and locked mode then fails.

### Support matrix

| RID | Artifact | Build host | Status |
|---|---|---|---|
| `linux-x64` | NativeAOT | Linux x64 runner with clang | Verified 2026-10-26 |
| `linux-arm64` | NativeAOT | Linux arm64 runner, or x64 with an arm64 cross sysroot | Planned |
| `win-x64` | NativeAOT | Windows runner with MSVC C++ build tools | Planned |
| `win-arm64` | NativeAOT | Windows x64 runner with the MSVC arm64 tools (cross-compile) | Planned |
| `osx-arm64` | NativeAOT | macOS runner | Gated with macOS CI (`TSKE014.2`) |
| `osx-x64` | NativeAOT | macOS runner (cross-compile from arm64) | Gated with macOS CI (`TSKE014.2`) |
| any | Framework-dependent portable | Any | Verified 2026-10-26 |

NativeAOT cannot cross-compile between operating systems, so each OS needs its own runner. Linux builds link against the build host's glibc; build on the oldest distribution to be supported. The current floor is glibc 2.34 (Debian 12, Ubuntu 22.04, RHEL 9), which is also what the NuGet-supplied `libe_sqlite3.so` requires.

## Consequences

- Any code that uses reflection-based serialization, `Type.GetType` lookups, or dynamic code fails the build. Fixes use the non-generic `JsonNode` APIs, `Utf8JsonWriter`, or System.Text.Json source generation.
- A package upgrade that introduces trim warnings blocks the upgrade until it is resolved or the package is replaced.
- Every new release RID must be added to `RuntimeIdentifiers` and the lock files regenerated with `dotnet restore --force-evaluate`.
- Musl-based Linux (Alpine, `linux-musl-x64`) is not built. Add it on request; it needs an Alpine build host and the musl SQLite native asset.
- Bundling FFmpeg into an artifact (`fluxb.ps1 -BundleFFmpegPath`, `TSKE015.7`) produces a separate, clearly named archive; its licensing obligations are decided in `TSKE015.5`.
- Archive naming, checksums, signatures, and SBOMs belong to `TSKE015.3`. Running the AOT smoke test automatically on each RID belongs to `TSKE015.2`.
