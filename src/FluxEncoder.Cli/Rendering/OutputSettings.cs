using FluxEncoder.Cli.Hosting;
using Spectre.Console;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// How output looks, resolved once from flags and the environment.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Colour is off with <c>--no-color</c>, a non-empty <c>NO_COLOR</c> (no-color.org), or <c>TERM=dumb</c>, and is
/// only ever written to a stream that is a terminal, so pipes and log files stay plain.</item>
/// <item>Animated progress needs an interactive stderr and is off in CI (<c>CI</c> set), with <c>TERM=dumb</c>,
/// and with <c>--quiet</c> or <c>--json</c>; non-animated runs print plain milestone lines instead.</item>
/// <item>Unicode symbols fall back to ASCII with <c>TERM=dumb</c> or <c>TERM=linux</c> (the Linux console font).</item>
/// </list>
/// </remarks>
internal sealed record OutputSettings(bool Json, bool Quiet, bool Verbose, bool Color, bool Animate, UiSymbols Symbols)
{
    public static OutputSettings Resolve(CliHost host, bool json, bool quiet, bool verbose, bool noColor)
    {
        ArgumentNullException.ThrowIfNull(host);

        var term = host.GetVariable("TERM");
        var dumb = string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
        var color = !noColor && !dumb && string.IsNullOrEmpty(host.GetVariable("NO_COLOR"));
        var ci = !string.IsNullOrEmpty(host.GetVariable("CI"));
        var animate = host.ErrorIsTerminal && !ci && !dumb && !quiet && !json;
        var symbols = dumb || string.Equals(term, "linux", StringComparison.OrdinalIgnoreCase) ? UiSymbols.Ascii : UiSymbols.Unicode;

        return new OutputSettings(json, quiet && !verbose, verbose, color, animate, symbols);
    }

    /// <summary>
    /// A Spectre console over <paramref name="writer"/> honouring the colour and animation decisions.
    /// </summary>
    /// <remarks>
    /// Non-animated consoles never wrap: Spectre would otherwise break long paths at 80 columns when output is piped,
    /// and terminals and log viewers wrap long lines themselves.
    /// </remarks>
    /// <param name="isTerminal">Whether <paramref name="writer"/> is a terminal.</param>
    public IAnsiConsole CreateConsole(TextWriter writer, bool isTerminal, bool interactive)
    {
        var color = Color && isTerminal;
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = isTerminal && (color || interactive) ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = color ? ColorSystemSupport.EightBit : ColorSystemSupport.NoColors,
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });

        if (!interactive)
        {
            console.Profile.Width = NoWrapWidth;
        }

        return console;
    }

    private const int NoWrapWidth = 4096;
}
