using System.Text.RegularExpressions;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Cli.Tests.Rendering;

/// <summary>
/// Snapshots of what people see (TSKE008.5): redirected output, colour-disabled and low-capability terminals, and
/// CI. Fake reports drive the reporter directly, so these run without FFmpeg and their output is exact.
/// </summary>
public sealed partial class HumanReporterTests
{
    private sealed record Captured(string Out, string Error);

    [GeneratedRegex(@"\u001b\[[0-9;?]*[A-Za-z]")]
    private static partial Regex ControlSequence();

    private static JobReport Report(string input, JobResult result) => new(
        input,
        new EncodingJob { InputPath = input, OutputPath = result.OutputPath, Video = new VideoTarget.StreamCopy(), Audio = new AudioTarget.StreamCopy() },
        result,
        null,
        null);

    private static JobProgress Progress(double? fraction, int seconds) =>
        new(1, 1, new EncodeProgress { ProcessedTime = TimeSpan.FromSeconds(seconds), Speed = 2.0 }, fraction);

    /// <summary>
    /// A three-input batch: one success with a warning, one skip, one failure.
    /// </summary>
    private static async Task<Captured> RunBatchAsync(Dictionary<string, string?> variables, bool outIsTerminal = false, bool errorIsTerminal = false, string? firstLabel = null, TimeSpan jobTime = default)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error, name => variables.GetValueOrDefault(name), errorIsTerminal, outIsTerminal);
        var reporter = new HumanReporter(host, OutputSettings.Resolve(host, json: false, quiet: false, verbose: false, noColor: false));

        JobReport[] reports =
        [
            Report(firstLabel ?? "a.mkv", JobResult.Succeeded("out/a.mkv", [new JobWarning("w", "Subtitles were dropped.")]) with { Elapsed = TimeSpan.FromSeconds(1.5) }),
            Report("b.mkv", JobResult.Skipped("out/b.mkv", "the output exists")),
            Report("c.mkv", JobResult.Failed("out/c.mkv", new JobFailure(FailureCategory.InvalidInput, "The input has no video stream."))),
        ];

        for (var i = 0; i < reports.Length; i++)
        {
            var report = reports[i];
            var finished = await reporter.RunJobAsync(i, reports.Length, report.Input, async progress =>
            {
                if (report.Result.Outcome == JobOutcome.Succeeded)
                {
                    progress.Report(Progress(0.5, 1));
                    await Task.Delay(jobTime, TestContext.Current.CancellationToken);
                    progress.Report(Progress(1.0, 2));
                }

                return report;
            });
            reporter.Completed(finished, dryRun: false, "ffmpeg");
        }

        reporter.Finished(reports, new BatchResult(reports.Select(r => r.Result)), dryRun: false);
        return new Captured(output.ToString(), error.ToString());
    }

    private const string PlainOut =
        "■ a.mkv → out/a.mkv  (1.5 s, video copied)\n" +
        "□ b.mkv: skipped, the output exists\n" +
        "1 succeeded, 1 skipped, 1 failed\n";

    private const string PlainError =
        "▶ [1/3] a.mkv\n" +
        "    50%  0:00:01  2.0x\n" +
        "    100%  0:00:02  2.0x\n" +
        "    ◆ Subtitles were dropped.\n" +
        "▶ [2/3] b.mkv\n" +
        "▶ [3/3] c.mkv\n" +
        "✕ c.mkv: The input has no video stream.\n";

    [Fact]
    public async Task Redirected_IsPlainTextWithMilestones()
    {
        var run = await RunBatchAsync([]);

        Assert.Equal(PlainOut, run.Out);
        Assert.Equal(PlainError, run.Error);
    }

    [Fact]
    public async Task DumbTerminal_IsAsciiOnly()
    {
        var run = await RunBatchAsync(new() { ["TERM"] = "dumb" }, outIsTerminal: true, errorIsTerminal: true);

        Assert.Equal(
            "+ a.mkv -> out/a.mkv  (1.5 s, video copied)\n" +
            "- b.mkv: skipped, the output exists\n" +
            "1 succeeded, 1 skipped, 1 failed\n",
            run.Out);
        Assert.StartsWith("> [1/3] a.mkv\n", run.Error, StringComparison.Ordinal);
        Assert.Contains("x c.mkv: The input has no video stream.\n", run.Error, StringComparison.Ordinal);
        Assert.All(run.Out + run.Error, c => Assert.True(c < 0x7F, $"U+{(int)c:X4} is not ASCII."));
    }

    [Theory]
    [InlineData("NO_COLOR")]
    [InlineData("TERM")]
    public async Task ColourDisabledTerminals_GetNoEscapeSequences(string variable)
    {
        var run = await RunBatchAsync(new() { [variable] = variable == "TERM" ? "dumb" : "1", ["CI"] = "1" }, outIsTerminal: true, errorIsTerminal: true);

        Assert.DoesNotContain('\u001b', run.Out + run.Error);
    }

    /// <summary>
    /// Colour only repeats what the symbols and words already say: with the escape sequences removed, a colour
    /// terminal shows exactly the plain text, so nothing depends on telling red from green.
    /// </summary>
    [Fact]
    public async Task Colour_CarriesNoInformationOfItsOwn()
    {
        var run = await RunBatchAsync(new() { ["CI"] = "1" }, outIsTerminal: true, errorIsTerminal: true);

        Assert.Contains('\u001b', run.Out);
        Assert.Equal(PlainOut, ControlSequence().Replace(run.Out, string.Empty));
        Assert.Equal(PlainError, ControlSequence().Replace(run.Error, string.Empty));
    }

    [Fact]
    public async Task Ci_UsesMilestonesEvenOnATerminal()
    {
        var run = await RunBatchAsync(new() { ["CI"] = "true", ["NO_COLOR"] = "1" }, errorIsTerminal: true);

        Assert.Equal(PlainError, run.Error);
    }

    /// <summary>
    /// The animated view escapes Spectre markup itself, so it is the one path where a label could be read as markup.
    /// The job runs long enough for Spectre to draw frames.
    /// </summary>
    [Fact]
    public async Task Animated_HostileLabelsStayLiteral()
    {
        var run = await RunBatchAsync([], errorIsTerminal: true, firstLabel: "[red]x[/] [link=https://evil]y[/]\u001b]0;pwned\u0007.mkv", jobTime: TimeSpan.FromMilliseconds(400));

        // The label is fitted to the console width, so only its start and end are certain to be drawn.
        Assert.Contains("Batch 1/3", run.Error, StringComparison.Ordinal);
        Assert.Contains("[red]x[/]", run.Error, StringComparison.Ordinal);
        Assert.Contains("?.mkv", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b]0;", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b]8;", run.Error, StringComparison.Ordinal);
        Assert.Contains("[red]x[/]", run.Out, StringComparison.Ordinal);
    }
}
