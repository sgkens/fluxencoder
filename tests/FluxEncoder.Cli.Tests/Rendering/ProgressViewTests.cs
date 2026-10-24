using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Cli.Tests.Rendering;

public sealed class ProgressViewTests
{
    [Theory]
    [InlineData(200, true, true, 40)]
    [InlineData(100, true, true, 40)]
    [InlineData(80, true, true, 20)]
    [InlineData(60, true, false, 10)]
    [InlineData(40, false, false, 0)]
    [InlineData(0, false, false, 0)]
    public void Layout_DropsColumnsAsTheTerminalNarrows(int width, bool bar, bool remaining, int barWidth)
    {
        var layout = ProgressLayout.For(width);

        Assert.Equal(bar, layout.ShowBar);
        Assert.Equal(remaining, layout.ShowRemaining);
        Assert.Equal(barWidth, layout.BarWidth);
    }

    [Fact]
    public void Layout_AlwaysLeavesRoomAndNeverOverflows()
    {
        for (var width = 0; width <= 300; width++)
        {
            var layout = ProgressLayout.For(width);
            var columns = layout.DescriptionCells + layout.BarWidth + (layout.ShowBar ? 4 + 1 : 0) + (layout.ShowRemaining ? 8 + 1 : 0) + 2;

            Assert.True(layout.DescriptionCells >= 18, $"{width}: only {layout.DescriptionCells} cells for the description.");
            Assert.True(columns < Math.Max(width, ProgressLayout.MinimumWidth) + 1, $"{width}: the columns need {columns} cells.");
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void Describe_FitsTheCellsItIsGiven(int cells)
    {
        var title = "[3/12] /media/映画/archive/very-long-directory-name/寿司-final.mkv";

        foreach (var symbols in new[] { UiSymbols.Unicode, UiSymbols.Ascii })
        {
            var described = ProgressView.Describe(title, "0:01:02  1.5x  12.0 MB", cells, symbols);
            Assert.True(DisplayWidth.Of(described) <= cells, $"'{described}' is wider than {cells}.");
        }
    }

    [Fact]
    public void Describe_ShortensTheTitleBeforeDroppingDetails()
    {
        var described = ProgressView.Describe("/media/archive/2024/holiday/final-cut.mkv", "0:01:02  1.5x", 40, UiSymbols.Ascii);

        Assert.EndsWith("final-cut.mkv  0:01:02  1.5x", described, StringComparison.Ordinal);
        Assert.Contains("...", described, StringComparison.Ordinal);

        var narrow = ProgressView.Describe("/media/archive/2024/holiday/final-cut.mkv", "0:01:02  1.5x", 20, UiSymbols.Ascii);
        Assert.DoesNotContain("1.5x", narrow, StringComparison.Ordinal);
        Assert.EndsWith(".mkv", narrow, StringComparison.Ordinal);
    }

    [Fact]
    public void Details_DescribeEveryKnownField()
    {
        var progress = new JobProgress(2, 2, new EncodeProgress { ProcessedTime = TimeSpan.FromSeconds(62), Speed = 1.5, TotalSizeBytes = 12 * 1024 * 1024 }, 0.5, Attempt: 2);

        Assert.Equal("pass 2/2  0:01:02  1.5x  12.0 MB  attempt 2", ProgressView.Details(progress));
        Assert.Empty(ProgressView.Details(new JobProgress(1, 1, new EncodeProgress(), null)));
    }

    [Fact]
    public void Batch_CountsOutcomes()
    {
        Assert.Equal("Batch 3/5  1 succeeded, 0 skipped, 1 failed", ProgressView.Batch(2, 5, 1, 0, 1, dryRun: false));
        Assert.Equal("Batch 1/2  0 planned, 0 skipped, 0 failed", ProgressView.Batch(0, 2, 0, 0, 0, dryRun: true));
    }
}
