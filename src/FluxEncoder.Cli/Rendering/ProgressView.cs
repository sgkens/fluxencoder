using System.Globalization;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// The one progress model every view draws from (TSKE008.4): the animated bar, the plain milestone lines written to
/// logs and CI, and the batch row all describe a <see cref="JobProgress"/> through these methods, so they never
/// disagree about what a run is doing.
/// </summary>
/// <remarks>
/// Three views follow from the data rather than from separate renderers:
/// <list type="bullet">
/// <item>Finite job: <see cref="JobProgress.Fraction"/> is known, so a bar, percentage, and time remaining are shown.</item>
/// <item>Live stream: no known length, so the bar stays indeterminate and the details carry media time processed,
/// speed, and size instead.</item>
/// <item>Batch: more than one input, so an extra row counts finished jobs and their outcomes.</item>
/// </list>
/// </remarks>
internal static class ProgressView
{
    /// <summary>
    /// Pass, media time, speed, size, and retry attempt, separated by two spaces; empty before the first report.
    /// </summary>
    public static string Details(JobProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var parts = new List<string>();
        if (value.PassCount > 1)
        {
            parts.Add($"pass {value.Pass}/{value.PassCount}");
        }

        if (value.Current.ProcessedTime is { } time)
        {
            parts.Add(time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture));
        }

        if (value.Current.Speed is { } speed)
        {
            parts.Add(speed.ToString("0.0", CultureInfo.InvariantCulture) + "x");
        }

        if (value.Current.TotalSizeBytes is { } size)
        {
            parts.Add((size / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB");
        }

        if (value.Attempt > 1)
        {
            parts.Add($"attempt {value.Attempt}");
        }

        return string.Join("  ", parts);
    }

    /// <summary>
    /// A title and its details in at most <paramref name="cells"/> cells. The details are what change, so the title is
    /// shortened first (keeping its end, where the file name is); the details are dropped only when even a short title
    /// would not fit beside them.
    /// </summary>
    /// <param name="title">Already made safe by <see cref="TerminalText.Safe"/>.</param>
    public static string Describe(string title, string details, int cells, UiSymbols symbols)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(symbols);

        const int Gap = 2;
        const int ShortestTitle = 12;

        if (details.Length == 0)
        {
            return DisplayWidth.Fit(title, cells, symbols.Ellipsis);
        }

        var detailsWidth = DisplayWidth.Of(details);
        var titleCells = cells - detailsWidth - Gap;
        if (titleCells < Math.Min(ShortestTitle, DisplayWidth.Of(title)))
        {
            return DisplayWidth.Fit(title, cells, symbols.Ellipsis);
        }

        return DisplayWidth.Fit(title, titleCells, symbols.Ellipsis) + new string(' ', Gap) + details;
    }

    /// <summary>
    /// The batch row: which input is running and how the finished ones went.
    /// </summary>
    public static string Batch(int index, int count, int succeeded, int skipped, int failed, bool dryRun) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Batch {index + 1}/{count}  {succeeded} {(dryRun ? "planned" : "succeeded")}, {skipped} skipped, {failed} failed");
}

/// <summary>
/// Which progress columns fit a terminal of a given width (TSKE008.2), replacing Spectre's fixed 40-cell bar so narrow
/// terminals and split panes never wrap the progress line, which would leave a trail of half-redrawn rows.
/// </summary>
/// <remarks>
/// Measured once when a job starts; a terminal resized mid-job keeps the layout it started with.
/// </remarks>
/// <param name="DescriptionCells">Cells left for the title and its details.</param>
/// <param name="BarWidth">Bar width in cells; 0 when <paramref name="ShowBar"/> is false.</param>
/// <param name="ShowBar">Whether the bar and percentage columns are shown.</param>
/// <param name="ShowRemaining">Whether the time-remaining column is shown.</param>
internal sealed record ProgressLayout(int DescriptionCells, int BarWidth, bool ShowBar, bool ShowRemaining)
{
    /// <summary>
    /// Below this, nothing fits usefully; the layout is computed as if the terminal were this wide.
    /// </summary>
    public const int MinimumWidth = 24;

    // Column widths as Spectre draws them: "100%" and "00:00:00", a one-cell spinner, one space between columns,
    // and one spare cell so a line never touches the right edge, where some terminals wrap early.
    private const int Percentage = 4;
    private const int Remaining = 8;
    private const int Spinner = 1;
    private const int Spare = 1;

    public static ProgressLayout For(int terminalWidth)
    {
        var width = Math.Max(terminalWidth, MinimumWidth);

        if (width >= 100)
        {
            return Build(width, bar: 40, remaining: true);
        }

        if (width >= 70)
        {
            return Build(width, bar: 20, remaining: true);
        }

        return width >= 50
            ? Build(width, bar: 10, remaining: false)
            : new ProgressLayout(width - Spinner - 1 - Spare, 0, ShowBar: false, ShowRemaining: false);
    }

    private static ProgressLayout Build(int width, int bar, bool remaining)
    {
        var columns = 2 + (remaining ? 1 : 0);
        var used = bar + Percentage + (remaining ? Remaining : 0) + Spinner + columns + Spare;
        return new ProgressLayout(width - used, bar, ShowBar: true, remaining);
    }
}
