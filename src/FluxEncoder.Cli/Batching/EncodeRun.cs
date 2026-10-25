using FluxEncoder.Cli.Commands;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;
using FluxEncoder.Infrastructure.Execution;
using FluxEncoder.Infrastructure.History;
using FluxEncoder.Infrastructure.Secrets;

namespace FluxEncoder.Cli.Batching;

/// <summary>
/// One run of encode jobs, shared by <c>encode</c> and <c>apply</c>: FFmpeg and FFprobe located once, the security
/// policy and limits applied, history opened once, and every planned output reserved so later jobs in the same run
/// cannot pick the same name.
/// </summary>
internal sealed class EncodeRun
{
    private readonly CliHost _host;
    private readonly CliSession _session;
    private readonly IReporter _reporter;
    private readonly EncodeEnvironment _environment;
    private readonly RunHistory? _history;
    private readonly HashSet<string> _reserved;

    private EncodeRun(CliHost host, CliSession session, IReporter reporter, EncodeEnvironment environment, RunHistory? history, HashSet<string> reserved, bool dryRun)
    {
        _host = host;
        _session = session;
        _reporter = reporter;
        _environment = environment;
        _history = history;
        _reserved = reserved;
        DryRun = dryRun;
    }

    public bool DryRun { get; }

    public static async Task<Result<EncodeRun>> StartAsync(
        CliHost host,
        CliSession session,
        IReporter reporter,
        string? ffmpeg,
        string? ffprobe,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var tools = await ToolSession.CreateAsync(ffmpeg, ffprobe, cancellationToken, session.Settings.Tools, host).ConfigureAwait(false);
        if (!tools.IsSuccess)
        {
            return tools.Failure;
        }

        var history = dryRun
            ? null
            : await RunHistory.OpenAsync(host, session.Settings.History, reporter, cancellationToken).ConfigureAwait(false);

        var reserved = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var managed = host.GetVariable(EncodeCommand.ManagedVariable) is "1" or "true";
        var policy = managed ? session.Settings.Security with { AllowExtraArguments = false } : session.Settings.Security;
        var limits = session.Settings.Limits;
        var environment = tools.Value with
        {
            ReservedOutputs = reserved,
            Policy = policy,
            Limits = limits,
            RunOptions = tools.Value.RunOptions with { MaxRunTime = limits.MaxRunTime },
        };

        return Result<EncodeRun>.Success(new EncodeRun(host, session, reporter, environment, history, reserved, dryRun));
    }

    /// <summary>
    /// Plans (dry run) or runs one job, records it in history, and reports it.
    /// </summary>
    /// <param name="label">What the progress line shows; the input unless the caller adds context.</param>
    public async Task<JobReport> RunAsync(int index, int count, string label, string input, EncodingJob job, CancellationToken cancellationToken)
    {
        if (RemoteMount.SplitRemoteInput(input) is { } remote)
        {
            var (resolved, remoteFailure) = await ResolveRemoteAsync(remote.Alias, remote.Path, cancellationToken).ConfigureAwait(false);
            if (remoteFailure is not null)
            {
                var failed = new JobReport(input, job, JobResult.Failed(job.OutputPath, remoteFailure), null, null);
                _reporter.Completed(failed, DryRun, _environment.FFmpegPath);
                return failed;
            }

            job = job with { InputPath = resolved! };
        }

        var started = DateTimeOffset.UtcNow;
        var telemetry = _history is { RecordsTelemetry: true } ? new TelemetryRecorder() : null;
        var report = DryRun
            ? await PlanAsync(input, job, cancellationToken).ConfigureAwait(false)
            : await _reporter.RunJobAsync(index, count, label, progress => EncodeAsync(input, job, Tee(progress, telemetry), cancellationToken)).ConfigureAwait(false);

        if (_history is not null)
        {
            await _history.RecordAsync(report, started, telemetry?.Samples ?? [], _environment.FFmpegPath, cancellationToken).ConfigureAwait(false);
        }

        if (report.Planned is { IsSkipped: false, Plan: { } plan })
        {
            _reserved.Add(Path.GetFullPath(plan.FinalOutputPath));
        }

        _reporter.Completed(report, DryRun, _environment.FFmpegPath);
        return report;
    }

    /// <summary>
    /// Turns <c>remote://ALIAS/path</c> into the real URL, with the password from the environment or the OS credential
    /// store (TSKE010.3). The password is registered for redaction before the URL exists, so it is masked in every
    /// message, JSON document, and history row. Users and paths are percent-encoded by <see cref="RemoteMount.ToUrl"/>.
    /// </summary>
    private async Task<(string? Url, JobFailure? Failure)> ResolveRemoteAsync(string alias, string path, CancellationToken cancellationToken)
    {
        if (!_session.Settings.Remotes.TryGetValue(alias, out var mount))
        {
            return (null, new JobFailure(FailureCategory.InvalidUsage, $"There is no remote '{alias}'. Add it with: remote add {alias} URL --password-stdin"));
        }

        string? password;
        try
        {
            password = await SecretStores.ReadAsync(SecretStores.RemoteKey(mount.Alias), _host.GetVariable, _host.SecretStore(), cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return (null, new JobFailure(FailureCategory.DependencyMissing, $"The password for remote '{alias}' could not be read: {ex.Message}"));
        }

        Redaction.Register(password);

        try
        {
            return (mount.ToUrl(path, password), null);
        }
        catch (ArgumentException ex)
        {
            return (null, new JobFailure(FailureCategory.InvalidUsage, $"remote://{alias}/{path}: {ex.Message}"));
        }
    }

    private static IProgress<JobProgress> Tee(IProgress<JobProgress> first, IProgress<JobProgress>? second) =>
        second is null ? first : new TeeProgress(first, second);

    private sealed class TeeProgress(IProgress<JobProgress> first, IProgress<JobProgress> second) : IProgress<JobProgress>
    {
        public void Report(JobProgress value)
        {
            first.Report(value);
            second.Report(value);
        }
    }

    private async Task<JobReport> EncodeAsync(string input, EncodingJob job, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        var outcome = await EncodeOrchestrator.RunAsync(job, _environment, progress, cancellationToken).ConfigureAwait(false);
        return new JobReport(input, job, outcome.Result, outcome.Planned, outcome.Strategy);
    }

    private async Task<JobReport> PlanAsync(string input, EncodingJob job, CancellationToken cancellationToken)
    {
        var (planned, strategy, _, warnings) = await EncodeOrchestrator.PlanAsync(job, _environment, cancellationToken).ConfigureAwait(false);
        if (!planned.IsSuccess)
        {
            return new JobReport(input, job, JobResult.Failed(job.OutputPath, planned.Failure, warnings), null, strategy);
        }

        var plan = planned.Value;
        var result = plan.IsSkipped
            ? JobResult.Skipped(plan.OutputPath, plan.SkipReason, [.. warnings, .. plan.Warnings])
            : JobResult.Succeeded(plan.OutputPath, [.. warnings, .. plan.Warnings]);

        return new JobReport(input, job, result, plan, strategy);
    }

    public async Task<JobReport> RunConcatAsync(string label, ConcatJob job, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var telemetry = _history is { RecordsTelemetry: true } ? new TelemetryRecorder() : null;
        var report = DryRun
            ? await PlanConcatAsync(label, job, cancellationToken).ConfigureAwait(false)
            : await _reporter.RunJobAsync(0, 1, label, progress => ConcatAsync(label, job, Tee(progress, telemetry), cancellationToken)).ConfigureAwait(false);

        if (_history is not null)
        {
            await _history.RecordAsync(report, started, telemetry?.Samples ?? [], _environment.FFmpegPath, cancellationToken).ConfigureAwait(false);
        }

        if (report.Planned is { IsSkipped: false, Plan: { } plan })
        {
            _reserved.Add(Path.GetFullPath(plan.FinalOutputPath));
        }

        _reporter.Completed(report, DryRun, _environment.FFmpegPath);
        return report;
    }

    private async Task<JobReport> ConcatAsync(string label, ConcatJob job, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        var outcome = await ConcatOrchestrator.RunAsync(job, _environment, progress, cancellationToken).ConfigureAwait(false);
        return new JobReport(label, job.ToEncodingJob(), outcome.Result, outcome.Planned, outcome.Strategy);
    }

    private async Task<JobReport> PlanConcatAsync(string label, ConcatJob job, CancellationToken cancellationToken)
    {
        var encodingJob = job.ToEncodingJob();
        var (planned, strategy, _, warnings) = await ConcatOrchestrator.PlanAsync(job, _environment, cancellationToken).ConfigureAwait(false);
        if (!planned.IsSuccess)
        {
            return new JobReport(label, encodingJob, JobResult.Failed(job.OutputPath, planned.Failure, warnings), null, strategy);
        }

        var plan = planned.Value;
        var result = plan.IsSkipped
            ? JobResult.Skipped(plan.OutputPath, plan.SkipReason, [.. warnings, .. plan.Warnings])
            : JobResult.Succeeded(plan.OutputPath, [.. warnings, .. plan.Warnings]);

        return new JobReport(label, encodingJob, result, plan, strategy);
    }
}
