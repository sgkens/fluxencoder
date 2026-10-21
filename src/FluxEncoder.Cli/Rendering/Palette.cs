using Spectre.Console;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// The single source of colours. Every styled element uses one of these, so the theme changes in one place and
/// colour-disabled output needs no special cases.
/// </summary>
internal static class Palette
{
    public static readonly Style Success = new(Color.Green);

    public static readonly Style Warning = new(Color.Yellow);

    public static readonly Style Failure = new(Color.Red);

    public static readonly Style Skipped = new(Color.Grey);

    public static readonly Style Accent = new(Color.DeepSkyBlue1);

    public static readonly Style Muted = new(Color.Grey);

    public static readonly Style Emphasis = new(decoration: Decoration.Bold);
}
