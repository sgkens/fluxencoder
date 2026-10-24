namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// Structural symbols, with ASCII fallbacks for terminals that cannot show Unicode. Plain geometric shapes only;
/// no emoji.
/// </summary>
internal sealed record UiSymbols(string Running, string Success, string Skipped, string Warning, string Failure, string Arrow, string Command, string Ellipsis)
{
    public static UiSymbols Unicode { get; } = new("▶", "■", "□", "◆", "✕", "→", "$", "…");

    public static UiSymbols Ascii { get; } = new(">", "+", "-", "!", "x", "->", "$", "...");
}
