using System.CommandLine;

namespace FluxEncoder.Cli;

/// <summary>
/// Builds the single command tree for the CLI. Commands are registered here only once
/// they are implemented and covered by tests (TSKE007.8).
/// </summary>
public static class CliApplication
{
    public static RootCommand BuildRootCommand() =>
        new("FluxEncoder: a terminal front end for FFmpeg encoding jobs.");

    public static int Run(string[] args) => BuildRootCommand().Parse(args).Invoke();
}
