using System.Globalization;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Infrastructure.Execution;

namespace FluxEncoder.Infrastructure.Tests.Execution;

public sealed class FFmpegProgressParserTests
{
    // Captured from FFmpeg 8.0.1 with -progress pipe:1.
    private const string Block = """
        frame=50
        fps=0.00
        stream_0_0_q=-1.0
        bitrate= 285.9kbits/s
        total_size=68622
        out_time_us=1920000
        out_time_ms=1920000
        out_time=00:00:01.920000
        dup_frames=0
        drop_frames=2
        speed=19.9x
        progress=end
        """;

    private static List<EncodeProgress> Feed(FFmpegProgressParser parser, string text) =>
        [.. text.Split('\n').Select(line => parser.Accept(line.TrimEnd('\r'))).OfType<EncodeProgress>()];

    [Fact]
    public void ParsesCompleteBlock()
    {
        var report = Assert.Single(Feed(new FFmpegProgressParser(), Block));

        Assert.Equal(50, report.Frame);
        Assert.Equal(0.0, report.FramesPerSecond);
        Assert.Equal(285.9, report.BitRateKbps);
        Assert.Equal(68622, report.TotalSizeBytes);
        Assert.Equal(TimeSpan.FromSeconds(1.92), report.ProcessedTime);
        Assert.Equal(19.9, report.Speed);
        Assert.Equal(0, report.DuplicatedFrames);
        Assert.Equal(2, report.DroppedFrames);
        Assert.True(report.IsFinal);
    }

    [Fact]
    public void EmitsOneReportPerBlockAndResetsBetweenBlocks()
    {
        var parser = new FFmpegProgressParser();

        var reports = Feed(parser, "frame=1\nspeed=1.5x\nprogress=continue\nout_time_us=500000\nprogress=continue\n");

        Assert.Equal(2, reports.Count);
        Assert.Equal(1, reports[0].Frame);
        Assert.False(reports[0].IsFinal);
        Assert.Null(reports[1].Frame);
        Assert.Null(reports[1].Speed);
        Assert.Equal(TimeSpan.FromMilliseconds(500), reports[1].ProcessedTime);
    }

    [Fact]
    public void TreatsNaAndNegativeAsUnknown()
    {
        const string Text = """
            frame=0
            bitrate=N/A
            total_size=N/A
            out_time_us=-9223372036854775807
            out_time_ms=-9223372036854775807
            out_time=-577014:32:22.775808
            speed=N/A
            progress=continue
            """;

        var report = Assert.Single(Feed(new FFmpegProgressParser(), Text));

        Assert.Equal(0, report.Frame);
        Assert.Null(report.BitRateKbps);
        Assert.Null(report.TotalSizeBytes);
        Assert.Null(report.ProcessedTime);
        Assert.Null(report.Speed);
    }

    [Fact]
    public void FallsBackToClockTime()
    {
        var report = Assert.Single(Feed(new FFmpegProgressParser(), "out_time=01:02:03.500000\nprogress=continue"));

        Assert.Equal(new TimeSpan(0, 1, 2, 3, 500), report.ProcessedTime);
    }

    [Fact]
    public void IgnoresNoiseLines()
    {
        var parser = new FFmpegProgressParser();

        Assert.Null(parser.Accept(null));
        Assert.Null(parser.Accept(""));
        Assert.Null(parser.Accept("garbage without equals"));
        Assert.Null(parser.Accept("=value"));
        Assert.NotNull(parser.Accept("progress=continue"));
    }

    [Fact]
    public void IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var report = Assert.Single(Feed(new FFmpegProgressParser(), Block));
            Assert.Equal(285.9, report.BitRateKbps);
            Assert.Equal(19.9, report.Speed);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
