# ADR 0002: CLI framework and package policy

FluxEncoder parses commands with System.CommandLine 2.0, renders terminal output with Spectre.Console, and pins every package version centrally with committed lock files.

- **Status:** Accepted
- **Date:** 2026-10-17
- **Tracker:** `TSKE001.3`

## Context

Two command-line designs exist in the repository:

- the legacy `CliApp` prototype uses a System.CommandLine 2.0 beta that no longer restores at its pinned version (`CODE_AUDIT.md` A-18);
- seven concept files in `FluxEncoderEngine/` use `Spectre.Console.Cli` command classes, whose `ExecuteAsync` overrides no longer match the current Spectre.Console.Cli API (`CODE_AUDIT.md` concept compilation probe).

The concept documents target NativeAOT single-binary distribution. Spectre.Console.Cli discovers commands and settings through reflection over generic type arguments and attributes, which produces trimming warnings and needs manual preservation under NativeAOT. System.CommandLine 2.0 is stable, is maintained alongside the .NET SDK, and is designed to run trimmed and under NativeAOT.

## Decision

1. **Parsing:** System.CommandLine 2.0 owns command, option, and argument parsing, help, and invocation. There is one root command, built in `FluxEncoder.Cli.CliApplication`.
2. **Rendering:** Spectre.Console is used for terminal output only (tables, progress, markup). `Spectre.Console.Cli` is not referenced. Concept command classes are ported to System.CommandLine handlers rather than compiled as-is.
3. **Package versions:** `Directory.Packages.props` declares every version (central package management with transitive pinning). Project files carry `PackageReference` without versions.
4. **Lock files:** every project commits `packages.lock.json`. Builds with `ContinuousIntegrationBuild=true` restore in locked mode and fail on drift.
5. **Stable releases only:** no prerelease packages. A new version is adopted only once it has been public for at least two weeks.
6. **Vulnerability audit:** NuGet audit runs on direct and transitive packages at severity `low` and above. With warnings treated as errors, an advisory fails the build.
7. **Testing:** xunit.v3 on Microsoft.Testing.Platform, selected in `global.json`. Test projects are self-hosting executables.

Pinned at adoption:

| Package | Version | Published |
|---|---|---|
| System.CommandLine | 2.0.12 | 2026-09-08 |
| Spectre.Console | 0.57.2 | 2026-07-02 |
| xunit.v3 | 4.0.1 | 2026-09-12 |
| Microsoft.Data.Sqlite | 10.0.12 | 2026-09-08 |
| YamlDotNet | 18.1.0 | 2026-06-26 |

## Consequences

- Command definitions are explicit code, so trimming and NativeAOT analysis apply to them without extra annotations.
- Help and parse errors come from System.CommandLine. Styling them with Spectre.Console needs a custom help action if the default output proves insufficient.
- Code coverage is not collected yet. `coverlet.collector` is a VSTest data collector and does not run under Microsoft.Testing.Platform; coverage is added with `Microsoft.Testing.Extensions.CodeCoverage` under `TSKE014.5`.
- Running `dotnet test` requires the `--solution` or `--project` form, because `global.json` opts the solution into the Microsoft.Testing.Platform experience.
