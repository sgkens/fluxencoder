using System.Globalization;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;
using Spectre.Console;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// Terminal output for people. Results go to stdout; progress, warnings, and errors go to stderr.
/// </summary>
/// <remarks>
/// Every dynamic value (paths, FFmpeg messages) is escaped before it reaches Spectre markup. Text is assembled and
/// padded first and styled last, so colour codes never disturb alignment.
/// </remarks>
internal sealed class HumanReporter : IReporter
{
    private static readonly TimeSpan MilestoneInterval = TimeSpan.FromSeconds(30);

    private readonly OutputSettings _settings;
    private readonly IAnsiConsole _out;
    private readonly IAnsiConsole _error;

    // Outcomes so far, for the batch row. Jobs run one at a time, so plain fields suffice.
    private int _succeeded;
    private int _skipped;
    private int _failed;
    private bool _dryRun;

    public HumanReporter(CliHost host, OutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(host);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _out = settings.CreateConsole(host.Out, host.OutIsTerminal, interactive: false);
        _error = settings.CreateConsole(host.Error, host.ErrorIsTerminal, interactive: settings.Animate);
    }

    private UiSymbols Symbols => _settings.Symbols;

    public void Fatal(JobFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        WriteLine(_error, $"{Symbols.Failure} {failure.Message}", Palette.Failure);
        if (failure.Detail is { Length: > 0 } detail && (_settings.Verbose || failure.Category == FailureCategory.DependencyMissing))
        {
            WriteLine(_error, Indent(detail), Palette.Muted);
        }
    }

    public void Notice(string message) => WriteLine(_error, $"{Symbols.Warning} {message}", Palette.Warning);

    public async Task<JobReport> RunJobAsync(int index, int count, string label, Func<IProgress<JobProgress>, Task<JobReport>> run)
    {
        var title = count > 1 ? $"[{index + 1}/{count}] {label}" : label;

        if (_settings.Quiet)
        {
            return await run(new Progress<JobProgress>(_ => { })).ConfigureAwait(false);
        }

        if (!_settings.Animate)
        {
            WriteLine(_error, $"{Symbols.Running} {title}", Palette.Accent);
            return await run(new MilestoneProgress(this)).ConfigureAwait(false);
        }

        var layout = ProgressLayout.For(_error.Profile.Width);
        var columns = new List<ProgressColumn> { new TaskDescriptionColumn { Alignment = Justify.Left } };
        if (layout.ShowBar)
        {
            columns.Add(new ProgressBarColumn { Width = layout.BarWidth });
            columns.Add(new PercentageColumn());
        }

        if (layout.ShowRemaining)
        {
            columns.Add(new RemainingTimeColumn());
        }

        columns.Add(new SpinnerColumn(Spinner.Known.Line));

        JobReport? report = null;
        await _error.Progress()
            .AutoClear(true)
            .HideCompleted(true)
            .Columns([.. columns])
            .StartAsync(async context =>
            {
                ProgressTask? batch = null;
                if (count > 1)
                {
                    batch = context.AddTask(Fit(ProgressView.Batch(index, count, _succeeded, _skipped, _failed, _dryRun), string.Empty, layout), maxValue: count);
                    batch.Value = index;
                }

                var job = new JobView(TerminalText.Safe(label), layout, Symbols, batch, index);
                var task = context.AddTask(job.Describe(string.Empty, null), maxValue: 1.0);
                task.IsIndeterminate = true;
                report = await run(new SpectreProgress(task, job)).ConfigureAwait(false);
                if (report.Result.Outcome == JobOutcome.Succeeded)
                {
                    task.IsIndeterminate = false;
                    task.Value = task.MaxValue;
                }

                task.StopTask();
                batch?.StopTask();
            }).ConfigureAwait(false);

        return report!;
    }

    public void Completed(JobReport report, bool dryRun, string ffmpegPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        var result = report.Result;
        _dryRun = dryRun;
        switch (result.Outcome)
        {
            case JobOutcome.Succeeded: _succeeded++; break;
            case JobOutcome.Skipped: _skipped++; break;
            default: _failed++; break;
        }

        if (dryRun && result.Outcome == JobOutcome.Succeeded && report.Planned?.Plan is { } plan)
        {
            WriteLine(_out, $"{Symbols.Success} {report.Input} {Symbols.Arrow} {plan.FinalOutputPath}", Palette.Success);
            WriteLine(_out, Indent(EncoderLine(report)), Palette.Muted);
            foreach (var invocation in plan.Invocations)
            {
                WriteLine(_out, Indent($"{Symbols.Command} {invocation.ToDisplayString(ffmpegPath)}"), Style.Plain);
            }
        }
        else if (result.Outcome == JobOutcome.Succeeded)
        {
            if (!_settings.Quiet)
            {
                WriteLine(_out, $"{Symbols.Success} {report.Input} {Symbols.Arrow} {result.OutputPath}  ({Seconds(result.Elapsed)}, {EncoderLine(report)})", Palette.Success);
            }
        }
        else if (result.Outcome == JobOutcome.Skipped)
        {
            if (!_settings.Quiet)
            {
                WriteLine(_out, $"{Symbols.Skipped} {report.Input}: skipped, {result.SkipReason}", Palette.Skipped);
            }
        }
        else
        {
            WriteLine(_error, $"{Symbols.Failure} {report.Input}: {result.Failure!.Message}", Palette.Failure);
            if (_settings.Verbose && result.Failure.Detail is { Length: > 0 } detail)
            {
                WriteLine(_error, Indent(detail), Palette.Muted);
            }
        }

        if (!_settings.Quiet)
        {
            foreach (var warning in result.Warnings)
            {
                WriteLine(_error, Indent($"{Symbols.Warning} {warning.Message}"), Palette.Warning);
            }
        }

        if (_settings.Verbose && !dryRun && report.Planned?.Plan is { } ran)
        {
            foreach (var invocation in ran.Invocations)
            {
                WriteLine(_error, Indent($"{Symbols.Command} {invocation.ToDisplayString(ffmpegPath)}"), Palette.Muted);
            }
        }
    }

    public void Finished(IReadOnlyList<JobReport> reports, BatchResult batch, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (_settings.Quiet || (reports.Count <= 1 && batch.NotStarted == 0))
        {
            return;
        }

        var parts = new List<string>
        {
            $"{batch.SucceededCount} {(dryRun ? "planned" : "succeeded")}",
            $"{batch.SkippedCount} skipped",
            $"{batch.FailedCount} failed",
        };

        if (batch.NotStarted > 0)
        {
            parts.Add($"{batch.NotStarted} not started");
        }

        var style = batch.FailedCount > 0 ? Palette.Failure : batch.NotStarted > 0 ? Palette.Warning : Palette.Success;
        WriteLine(_out, string.Join(", ", parts), style);
    }

    private static string EncoderLine(JobReport report) => report.VideoEncoder switch
    {
        null => "no video",
        "copy" => "video copied",
        var encoder => report.Strategy is { } strategy ? $"{encoder} on {strategy.DisplayName}" : encoder,
    };

    private static string Seconds(TimeSpan elapsed) =>
        elapsed.TotalSeconds < 60
            ? elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(line => "    " + line.TrimEnd('\r')));

    /// <summary>
    /// Writes plain text in one style: secrets masked and control characters neutralized first, styled last.
    /// </summary>
    private static void WriteLine(IAnsiConsole console, string text, Style style) =>
        console.Write(new Text(TerminalText.Safe(text) + "\n", style));

    /// <summary>
    /// Fits a description to the layout and escapes it for Spectre markup, in that order: escaping doubles brackets,
    /// which would otherwise count towards the width.
    /// </summary>
    private string Fit(string title, string details, ProgressLayout layout) =>
        Markup.Escape(ProgressView.Describe(title, details, layout.DescriptionCells, Symbols));

    /// <summary>
    /// One job's row in the animated view, and its share of the batch row when there is one.
    /// </summary>
    private sealed record JobView(string Title, ProgressLayout Layout, UiSymbols Symbols, ProgressTask? Batch, int Index)
    {
        public string Describe(string details, double? fraction)
        {
            // Without a bar column the percentage moves into the description, ahead of the other details.
            if (!Layout.ShowBar && fraction is { } known)
            {
                var percent = ((int)(known * 100)).ToString(CultureInfo.InvariantCulture) + "%";
                details = details.Length > 0 ? $"{percent}  {details}" : percent;
            }

            return Markup.Escape(ProgressView.Describe(Title, details, Layout.DescriptionCells, Symbols));
        }
    }

    /// <summary>
    /// Progress for animated terminals: a bar with ETA for finite inputs, a spinner with elapsed media time, speed,
    /// and size for inputs without a known length.
    /// </summary>
    private sealed class SpectreProgress(ProgressTask task, JobView view) : IProgress<JobProgress>
    {
        public void Report(JobProgress value)
        {
            if (value.Fraction is { } fraction)
            {
                task.IsIndeterminate = false;
                task.Value = fraction;
                if (view.Batch is { } batch)
                {
                    batch.Value = view.Index + fraction;
                }
            }

            task.Description = view.Describe(ProgressView.Details(value), value.Fraction);
        }
    }

    /// <summary>
    /// Progress for logs and CI: one line at each quarter for finite inputs, and every 30 seconds otherwise, after
    /// one line explaining why there is no percentage.
    /// </summary>
    private sealed class MilestoneProgress(HumanReporter reporter) : IProgress<JobProgress>
    {
        private readonly object _gate = new();
        private int _nextQuarter = 1;
        private DateTime _nextTimed = DateTime.UtcNow + MilestoneInterval;
        private bool _explained;

        public void Report(JobProgress value)
        {
            string? line = null;

            lock (_gate)
            {
                if (value.Fraction is { } fraction)
                {
                    if (fraction >= _nextQuarter / 4.0 && _nextQuarter <= 4)
                    {
                        _nextQuarter = Math.Min(4, (int)(fraction * 4)) + 1;
                        line = $"{(int)(fraction * 100)}%  {ProgressView.Details(value)}";
                    }
                }
                else if (!_explained)
                {
                    _explained = true;
                    line = "The input has no known length, so progress shows media time processed instead of a percentage.";
                }
                else if (DateTime.UtcNow >= _nextTimed)
                {
                    _nextTimed = DateTime.UtcNow + MilestoneInterval;
                    line = ProgressView.Details(value);
                }
            }

            if (line is not null)
            {
                WriteLine(reporter._error, Indent(line.TrimEnd()), Palette.Muted);
            }
        }
    }
}
