using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluxEncoder.Cli.Batching;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Manifests;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;
using FluxEncoder.Infrastructure.Configuration;
using FluxEncoder.Infrastructure.Manifests;

namespace FluxEncoder.Cli.Commands;

/// <summary>
/// <c>apply</c>: runs the encode jobs in a Fluxfile (FMT-E011). Each job is parsed through the <c>encode</c> command
/// itself, so a Fluxfile accepts exactly the options <c>encode</c> does, validated the same way.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Every job is validated before any runs; <c>--check</c> stops there and needs no FFmpeg. A Fluxfile without
/// jobs is an error, never an empty success.</item>
/// <item>Re-running is safe: jobs skip outputs that already exist unless the job or <c>--overwrite</c> says
/// otherwise.</item>
/// <item>A job whose needs failed does not start; independent jobs still run unless <c>--fail-fast</c>.</item>
/// <item>A Fluxfile cannot choose the FFmpeg executable, pass raw FFmpeg arguments, read environment variables other
/// than <c>FLUX_*</c>, or write outside its directory without <c>--allow-outside</c>. The encode defaults in
/// <c>settings.json</c> do not apply, so a Fluxfile means the same on every machine; presets it names do.</item>
/// </list>
/// </remarks>
internal static class ApplyCommand
{
    public const string Name = "apply";

    private const string VariablePrefix = "FLUX_";

    /// <summary>
    /// Encode options a Fluxfile may not set, with the reason shown to the user.
    /// </summary>
    internal static readonly ImmutableDictionary<string, string> Refused = new Dictionary<string, string>
    {
        ["ffmpeg-arg"] = "raw FFmpeg arguments can read or write any file. Use 'encode --ffmpeg-arg' for one-off runs.",
        ["ffmpeg"] = "a Fluxfile cannot choose which program runs. Pass --ffmpeg to apply.",
        ["ffprobe"] = "a Fluxfile cannot choose which program runs. Pass --ffprobe to apply.",
        ["output"] = "use the job key 'output'.",
        ["preset"] = "use the job key 'preset'.",
        ["dry-run"] = "pass --dry-run to apply.",
        ["fail-fast"] = "pass --fail-fast to apply.",
        ["json"] = "pass --json to apply.",
        ["quiet"] = "pass --quiet to apply.",
        ["verbose"] = "pass --verbose to apply.",
        ["no-color"] = "pass --no-color to apply.",
        ["no-config"] = "Fluxfiles never use the encode defaults in settings.json.",
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly string[] OverwriteOptions = ["overwrite", "skip-existing", "rename-existing"];

    public static Command Build(CliHost host, CliSession session)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(session);

        var file = new Argument<string?>("fluxfile")
        {
            Description = "The Fluxfile (.yaml, .yml, or .json). Default: Fluxfile.yaml, Fluxfile.yml, or Fluxfile.json in the current directory.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var jobs = new Option<string[]>("--job") { Description = "Run only this job and the jobs it needs; repeat for more." };
        var variables = new Option<string[]>("--var")
        {
            Description = "Set ${NAME} in the Fluxfile: --var NAME=VALUE; repeat for more. Environment variables are used only when their names start with FLUX_.",
        };
        var check = new Option<bool>("--check") { Description = "Validate the Fluxfile and every job's options, then stop. Needs no FFmpeg and touches no file." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Probe and plan every job, show the FFmpeg commands (secrets masked), and stop." };
        var overwrite = new Option<bool>("--overwrite") { Description = "Redo jobs whose output exists. Default: skip them, so running apply again only does what is missing." };
        var allowOutside = new Option<bool>("--allow-outside") { Description = "Allow outputs outside the Fluxfile's directory." };
        var failFast = new Option<bool>("--fail-fast") { Description = "Stop at the first failure." };
        var json = new Option<bool>("--json") { Description = "Write one JSON document to stdout and nothing else." };
        var quiet = new Option<bool>("--quiet") { Description = "Print errors only." };
        var verbose = new Option<bool>("--verbose") { Description = "Also print FFmpeg commands and full error output." };
        var noColor = new Option<bool>("--no-color") { Description = "Disable colour (also NO_COLOR=1)." };
        var ffmpeg = new Option<string?>("--ffmpeg") { Description = "FFmpeg executable or its directory." };
        var ffprobe = new Option<string?>("--ffprobe") { Description = "FFprobe executable or its directory." };

        variables.Validators.Add(result =>
        {
            foreach (var value in result.GetValueOrDefault<string[]>() ?? [])
            {
                if (SplitVariable(value) is null)
                {
                    result.AddError($"--var takes NAME=VALUE with a name of letters, digits, and _; got '{value}'.");
                }
            }
        });

        var command = new Command(Name, "Run the encode jobs described in a Fluxfile (YAML or JSON).")
        {
            file, jobs, variables, check, dryRun, overwrite, allowOutside, failFast, json, quiet, verbose, noColor, ffmpeg, ffprobe,
        };

        command.Validators.Add(result =>
        {
            if (result.GetValue(check) && result.GetValue(dryRun))
            {
                result.AddError("Choose either --check or --dry-run.");
            }

            if (result.GetValue(json) && (result.GetValue(quiet) || result.GetValue(verbose)))
            {
                result.AddError("--json already prints only the JSON document; it cannot be combined with --quiet or --verbose.");
            }

            if (result.GetValue(quiet) && result.GetValue(verbose))
            {
                result.AddError("Choose either --quiet or --verbose.");
            }
        });

        command.SetAction((parse, token) => new Apply(host, session, parse.GetValue(file), new Options(
            [.. parse.GetValue(jobs) ?? []],
            (parse.GetValue(variables) ?? []).Select(SplitVariable).OfType<KeyValuePair<string, string>>().ToImmutableDictionary(StringComparer.Ordinal),
            parse.GetValue(check),
            parse.GetValue(dryRun),
            parse.GetValue(overwrite),
            parse.GetValue(allowOutside),
            parse.GetValue(failFast),
            OutputSettings.Resolve(host, parse.GetValue(json), parse.GetValue(quiet), parse.GetValue(verbose), parse.GetValue(noColor)),
            parse.GetValue(ffmpeg),
            parse.GetValue(ffprobe))).RunAsync(token));

        return command;
    }

    /// <summary>
    /// The encode options a Fluxfile job may set, by name without dashes. <c>docs/schema/fluxfile-v1.schema.json</c>
    /// lists the same set; a test keeps the two in step.
    /// </summary>
    internal static ImmutableDictionary<string, FluxOptionKind> ManifestOptions(Command encode) =>
        encode.Options
            .Where(o => o.Name.StartsWith("--", StringComparison.Ordinal) && !Refused.ContainsKey(o.Name[2..]))
            .ToImmutableDictionary(o => o.Name[2..], o => o.ValueType == typeof(bool) ? FluxOptionKind.Flag : FluxOptionKind.Value, StringComparer.Ordinal);

    private static KeyValuePair<string, string>? SplitVariable(string text)
    {
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        return equals > 0 && ManifestVariables.IsValidName(text[..equals]) ? new(text[..equals], text[(equals + 1)..]) : null;
    }

    private sealed record Options(
        ImmutableArray<string> Jobs,
        ImmutableDictionary<string, string> Variables,
        bool Check,
        bool DryRun,
        bool Overwrite,
        bool AllowOutside,
        bool FailFast,
        OutputSettings Output,
        string? FFmpeg,
        string? FFprobe);

    /// <summary>
    /// One invocation: load, validate, then run in dependency order.
    /// </summary>
    private sealed class Apply(CliHost host, CliSession session, string? fileArgument, Options options)
    {
        private readonly (EncodeCommand Definition, Command Command) _encode = EncodeCommand.Define(host, session);

        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            IReporter reporter = options.Output.Json ? new JsonReporter(host) : new HumanReporter(host, options.Output);

            var path = fileArgument ?? FluxfileLoader.FindDefault(Directory.GetCurrentDirectory());
            if (path is null)
            {
                return Fatal(reporter, $"No Fluxfile here; looked for {string.Join(", ", FluxfileLoader.DefaultNames)}. Name one: apply PATH.");
            }

            var display = fileArgument ?? Path.GetFileName(path);
            var (settings, warnings) = SettingsStore.Load(host.Paths.SettingsFile);
            session.Settings = settings;
            foreach (var warning in warnings)
            {
                reporter.Notice(warning);
            }

            var (fluxfile, errors) = FluxfileLoader.Load(new FluxfileReadOptions
            {
                Path = path,
                EncodeOptions = ManifestOptions(_encode.Command),
                RefusedOptions = Refused,
                Variables = Variable,
                AllowOutputsOutside = options.AllowOutside,
            });

            if (fluxfile is null)
            {
                return Invalid(reporter, display, [.. errors.Select(e => e.Format(display))]);
            }

            ImmutableArray<FluxJob> order;
            try
            {
                order = fluxfile.ExecutionOrder(options.Jobs);
            }
            catch (ArgumentException ex)
            {
                var cut = ex.Message.IndexOf(" (Parameter", StringComparison.Ordinal);
                return Fatal(reporter, cut > 0 ? ex.Message[..cut] : ex.Message);
            }

            // Every job's options must parse before anything runs; inputs from other jobs are not known yet, so a
            // placeholder stands in for them.
            var problems = order
                .Select(job => (job, Error: Parse(job, job.Inputs.Select(i => i.Path ?? "placeholder.mkv")).Error))
                .Where(p => p.Error is not null)
                .Select(p => $"{display}:{p.job.Position}: job '{p.job.Name}': {p.Error}")
                .ToList();
            if (problems.Count > 0)
            {
                return Invalid(reporter, display, problems);
            }

            foreach (var notice in session.Notices.Distinct(StringComparer.Ordinal))
            {
                reporter.Notice(notice);
            }

            if (options.Check)
            {
                WriteValid(display, fluxfile, order);
                return (int)ExitCode.Success;
            }

            return await RunJobsAsync(reporter, order, cancellationToken).ConfigureAwait(false);
        }

        private string? Variable(string name) =>
            options.Variables.TryGetValue(name, out var value) ? value
            : name.StartsWith(VariablePrefix, StringComparison.Ordinal) ? host.GetVariable(name)
            : null;

        private async Task<int> RunJobsAsync(IReporter reporter, ImmutableArray<FluxJob> order, CancellationToken cancellationToken)
        {
            var run = await EncodeRun.StartAsync(host, session, reporter, options.FFmpeg, options.FFprobe, options.DryRun, cancellationToken).ConfigureAwait(false);
            if (!run.IsSuccess)
            {
                reporter.Fatal(run.Failure);
                return (int)run.Failure.ExitCode;
            }

            var outputs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var unfinished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reports = new List<JobReport>();
            var otherFailures = new List<JobResult>();
            var notStarted = 0;

            for (var index = 0; index < order.Length; index++)
            {
                var job = order[index];
                if (cancellationToken.IsCancellationRequested)
                {
                    notStarted += order.Length - index;
                    break;
                }

                if (job.Needs.FirstOrDefault(unfinished.Contains) is { } blocker)
                {
                    reporter.Notice($"Job '{job.Name}' did not start: it needs '{blocker}', which did not finish.");
                    unfinished.Add(job.Name);
                    notStarted++;
                    continue;
                }

                var inputs = job.Inputs.SelectMany(i => i.FromJob is { } from ? outputs[from] : [i.Path!]).ToList();
                var (parse, error) = Parse(job, inputs);
                var (created, usage) = error is null ? _encode.Definition.CreateJobs(parse!) : ([], new JobFailure(FailureCategory.InvalidUsage, error));
                if (usage is not null)
                {
                    reporter.Notice($"Job '{job.Name}': {usage.Message}");
                    otherFailures.Add(JobResult.Failed(job.Output ?? job.Name, usage));
                    unfinished.Add(job.Name);
                    if (options.FailFast)
                    {
                        notStarted += order.Length - index - 1;
                        break;
                    }

                    continue;
                }

                var produced = outputs[job.Name] = [];
                var failed = false;
                var stop = false;
                foreach (var (input, encodingJob) in created)
                {
                    var label = $"{job.Name}: {input}";
                    var report = options.DryRun && !Exists(input) && job.Inputs.Any(i => i.FromJob is not null)
                        ? Waiting(input, encodingJob, job)
                        : await run.Value.RunAsync(index, order.Length, label, input, encodingJob, cancellationToken).ConfigureAwait(false);

                    reports.Add(report);
                    if (report.Result.Outcome is JobOutcome.Succeeded or JobOutcome.Skipped)
                    {
                        produced.Add(report.Result.OutputPath);
                        continue;
                    }

                    failed = true;
                    if (report.Result.Failure is { Category: FailureCategory.Cancelled } || options.FailFast)
                    {
                        stop = true;
                        break;
                    }
                }

                if (failed)
                {
                    unfinished.Add(job.Name);
                }

                if (stop)
                {
                    notStarted += order.Length - index - 1;
                    break;
                }
            }

            var batch = new BatchResult(reports.Select(r => r.Result).Concat(otherFailures)) { NotStarted = notStarted };
            reporter.Finished(reports, batch, options.DryRun);
            return (int)batch.ExitCode;

            JobReport Waiting(string input, EncodingJob encodingJob, FluxJob job)
            {
                var from = string.Join(", ", job.Inputs.Where(i => i.FromJob is not null).Select(i => $"'{i.FromJob}'"));
                var report = new JobReport(input, encodingJob, JobResult.Skipped(encodingJob.OutputPath, $"its input is written by {from}, so it is planned once that has run"), null, null);
                reporter.Completed(report, dryRun: true, "ffmpeg");
                return report;
            }
        }

        private static bool Exists(string input) =>
            Core.Media.InputSource.Classify(input).LocalPath is not { } local || File.Exists(local) || Directory.Exists(local);

        /// <summary>
        /// Parses one job through the encode command. Values travel as single <c>--name=value</c> tokens and inputs
        /// after <c>--</c>, so no value from the file can be read as another option; refused options are checked
        /// again on the result as a second line of defence.
        /// </summary>
        private (ParseResult? Parse, string? Error) Parse(FluxJob job, IEnumerable<string> inputs)
        {
            var arguments = new List<string>();
            var jobOptions = job.Options.Where(o => !options.Overwrite || !OverwriteOptions.Contains(o.Name));
            foreach (var option in jobOptions)
            {
                arguments.Add(option.Value is null ? $"--{option.Name}" : $"--{option.Name}={option.Value}");
            }

            if (options.Overwrite)
            {
                arguments.Add("--overwrite");
            }
            else if (!job.Options.Any(o => OverwriteOptions.Contains(o.Name)))
            {
                arguments.Add("--skip-existing");
            }

            if (job.Output is { } output)
            {
                arguments.Add($"--output={output}");
            }

            var command = _encode.Command;
            if (job.Preset is { } preset)
            {
                var first = command.Parse(arguments);
                var merged = SettingsMerge.Apply(first, command, arguments, session.Settings, preset, useSettings: false);
                if (merged.Error is { } mergeError)
                {
                    return (null, mergeError);
                }

                foreach (var notice in merged.Notices)
                {
                    session.Notices.Add(notice);
                }

                arguments = [.. merged.Arguments];
            }

            arguments.Add("--");
            arguments.AddRange(inputs);

            var parse = command.Parse(arguments);
            if (parse.Errors.Count > 0)
            {
                return (null, string.Join(" ", parse.Errors.Select(e => e.Message)));
            }

            var smuggled = command.Options.FirstOrDefault(o => o.Name.StartsWith("--", StringComparison.Ordinal)
                && Refused.ContainsKey(o.Name[2..]) && o.Name is not "--output"
                && parse.GetResult(o) is { Implicit: false });
            return smuggled is null ? (parse, null) : (null, $"{smuggled.Name} cannot be set from a Fluxfile.");
        }

        private int Invalid(IReporter reporter, string display, IReadOnlyList<string> problems)
        {
            var heading = problems.Count == 1 ? $"{display} has 1 problem:" : $"{display} has {problems.Count} problems:";
            return Fatal(reporter, string.Join('\n', problems.Prepend(heading)));
        }

        private static int Fatal(IReporter reporter, string message)
        {
            var failure = new JobFailure(FailureCategory.InvalidUsage, message);
            reporter.Fatal(failure);
            return (int)failure.ExitCode;
        }

        private void WriteValid(string display, Fluxfile fluxfile, ImmutableArray<FluxJob> order)
        {
            if (!options.Output.Json)
            {
                var count = order.Length == 1 ? "1 job" : $"{order.Length} jobs";
                host.Out.WriteLine(TerminalText.Safe($"{display} is valid: {count}, run in this order: {string.Join(", ", order.Select(j => j.Name))}."));
                return;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("exitCode", (int)ExitCode.Success);
                writer.WriteString("file", Redaction.Text(fluxfile.Path));
                writer.WriteNumber("version", fluxfile.Version);
                writer.WriteStartArray("jobs");
                foreach (var job in order)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", job.Name);
                    writer.WriteStartArray("inputs");
                    foreach (var input in job.Inputs)
                    {
                        writer.WriteStringValue(Redaction.Text(input.ToString()));
                    }

                    writer.WriteEndArray();
                    if (job.Output is { } output)
                    {
                        writer.WriteString("output", Redaction.Text(output));
                    }
                    else
                    {
                        writer.WriteNull("output");
                    }

                    writer.WriteStartArray("needs");
                    foreach (var need in job.Needs)
                    {
                        writer.WriteStringValue(need);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            host.Out.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
    }
}
