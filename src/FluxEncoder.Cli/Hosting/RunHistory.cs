using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;
using FluxEncoder.Infrastructure.Configuration;
using FluxEncoder.Infrastructure.History;
using Microsoft.Data.Sqlite;

namespace FluxEncoder.Cli.Hosting;

/// <summary>
/// Records finished runs for one CLI invocation. History is a convenience: if it cannot be opened or written, the
/// user is told once and encoding carries on unaffected.
/// </summary>
internal sealed class RunHistory
{
    private readonly HistoryStore? _store;
    private readonly HistorySettings _settings;
    private readonly IReporter _reporter;
    private bool _writeFailureReported;

    private RunHistory(HistoryStore? store, HistorySettings settings, IReporter reporter)
    {
        _store = store;
        _settings = settings;
        _reporter = reporter;
    }

    public bool RecordsTelemetry => _store is not null && _settings.Telemetry;

    public static async Task<RunHistory> OpenAsync(CliHost host, HistorySettings settings, IReporter reporter, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return new RunHistory(null, settings, reporter);
        }

        try
        {
            var (database, notice) = await HistoryDatabase.OpenAsync(host.Paths.HistoryDatabase, cancellationToken).ConfigureAwait(false);
            if (notice is not null)
            {
                reporter.Notice(notice);
            }

            return new RunHistory(database.ReadOnly ? null : new HistoryStore(database), settings, reporter);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            reporter.Notice($"Run history could not be opened ({ex.Message}); this run is not recorded.");
            return new RunHistory(null, settings, reporter);
        }
    }

    public async Task RecordAsync(JobReport report, DateTimeOffset started, IReadOnlyList<TelemetrySample> samples, string ffmpegPath, CancellationToken cancellationToken)
    {
        if (_store is null)
        {
            return;
        }

        var result = report.Result;
        var run = new RunRecord
        {
            Id = RunRecord.NewId(),
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Input = Redaction.Text(report.Input),
            Output = Redaction.Text(result.OutputPath),
            Outcome = JsonReporter.Kebab(result.Outcome.ToString()),
            ExitCode = (int)result.ExitCode,
            FailureCategory = result.Failure is { } failure ? JsonReporter.Kebab(failure.Category.ToString()) : null,
            FailureMessage = result.Failure?.Message is { } message ? Redaction.Text(message) : null,
            VideoEncoder = report.VideoEncoder,
            Hardware = report.Strategy is { } strategy ? JsonReporter.Kebab(strategy.Vendor.ToString()) : null,
            Attempts = result.Attempts,
            ElapsedSeconds = Math.Round(result.Elapsed.TotalSeconds, 3),
            InputBytes = FileSize(InputSource.Classify(report.Input).LocalPath),
            OutputBytes = result.Outcome == JobOutcome.Succeeded ? FileSize(result.OutputPath) : null,
            Command = report.Planned?.Plan?.Invocations[^1].ToDisplayString(ffmpegPath) is { } command ? Redaction.Text(command) : null,
            WarningCodes = [.. result.Warnings.Select(w => w.Code)],
        };

        try
        {
            // Not cancellable: a run that finished is recorded even while the batch is being stopped.
            await _store.RecordAsync(run, RecordsTelemetry ? samples : [], _settings, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!_writeFailureReported)
            {
                _writeFailureReported = true;
                _reporter.Notice($"Run history could not be written ({ex.Message}); encoding is unaffected.");
            }
        }
    }

    private static long? FileSize(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
