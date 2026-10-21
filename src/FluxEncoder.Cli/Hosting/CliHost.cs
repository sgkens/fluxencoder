namespace FluxEncoder.Cli.Hosting;

/// <summary>
/// The process surroundings the CLI reads and writes, injectable so commands run identically in tests.
/// </summary>
/// <param name="Out">Results: the summary, the dry-run plan, or the JSON document.</param>
/// <param name="Error">Diagnostics: progress, warnings, and errors.</param>
/// <param name="GetVariable">Environment lookup (<c>NO_COLOR</c>, <c>CI</c>, <c>TERM</c>).</param>
/// <param name="ErrorIsTerminal">Whether <paramref name="Error"/> is an interactive terminal, which enables animation.</param>
/// <param name="OutIsTerminal">Whether <paramref name="Out"/> is a terminal; colour is only written to terminals.</param>
internal sealed record CliHost(TextWriter Out, TextWriter Error, Func<string, string?> GetVariable, bool ErrorIsTerminal, bool OutIsTerminal = false)
{
    public static CliHost Console { get; } = new(
        System.Console.Out,
        System.Console.Error,
        Environment.GetEnvironmentVariable,
        !System.Console.IsErrorRedirected,
        !System.Console.IsOutputRedirected);
}
