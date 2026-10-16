# ADR 0001: Target framework policy

FluxEncoder targets .NET 10 (`net10.0`) only, and builds with the .NET 10 SDK pinned in `global.json`.

- **Status:** Accepted
- **Date:** 2026-10-17
- **Tracker:** `TSKE001.1`

## Context

The repository mixed two framework targets:

- the legacy prototype `CliApp/FluxEncoderCli.csproj` targets `net8.0` with a fixed `win-x64` runtime identifier, and does not compile (`CODE_AUDIT.md` A-02);
- the concept sources and design references in `FluxEncoderEngine/` describe .NET 10 with NativeAOT publishing.

Both the host (SDK 10.0.401) and the `phellams-automator:localbuild` image (SDK 8.0.424 and 10.0.400) provide a .NET 10 SDK.

Nothing currently requires .NET 8 compatibility:

- `psm/FluxEncoder.psm1` is empty. A PowerShell wrapper is expected to invoke the published executable as a separate process, not load FluxEncoder assemblies into the PowerShell host, so the PowerShell runtime version does not constrain the FluxEncoder target.
- No library consumers, plugins, or published packages exist.
- No deployment environment with a .NET 8-only runtime has been named.

## Decision

1. All production and test projects target `net10.0` (LTS, supported until November 2028). The value is set once in `Directory.Build.props`.
2. `net8.0` is not multi-targeted. It is added only if a concrete compatibility requirement is recorded in a new ADR that supersedes this one.
3. `global.json` pins the 10.0 SDK feature band with `rollForward: latestFeature`, so builds use any 10.0.x SDK at or above `10.0.100` and never silently move to a later major SDK.
4. Normal builds do not set a `RuntimeIdentifier`. Runtime identifiers and NativeAOT are publish-time choices, decided per artifact under `TSKE015.1`.
5. The legacy `CliApp` project leaves the solution build. Its classification (merge input or archive) is handled by `TSKE001.4`.

## Consequences

- One framework means one set of nullable, trimming, and AOT analyzer results. There is no conditional compilation for older runtimes.
- Contributors and CI need a .NET 10 SDK. The .NET 8 SDK in the build image is no longer used and can be dropped when `TSKE014.3` pins the image.
- Choosing NativeAOT later remains open, because `net10.0` supports it on Windows, Linux, and macOS for x64 and arm64.
- Re-evaluate this decision before .NET 10 reaches end of support, moving to the next LTS release.
