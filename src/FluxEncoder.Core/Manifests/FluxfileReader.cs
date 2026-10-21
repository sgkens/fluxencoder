using System.Collections.Immutable;
using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Manifests;

public enum FluxOptionKind
{
    /// <summary>
    /// Takes a value: <c>codec: hevc</c>. A sequence is joined with commas: <c>audio: [jpn, eng]</c>.
    /// </summary>
    Value,

    /// <summary>
    /// On or off: <c>two-pass: true</c>. Only <c>true</c> and <c>false</c> are accepted.
    /// </summary>
    Flag,
}

public sealed record FluxfileReadOptions
{
    /// <summary>
    /// The manifest's full path. Relative paths in it resolve against its directory, never the working directory, so a
    /// Fluxfile means the same thing wherever it is run from.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Encode options a job or <c>defaults</c> may set, by name without dashes.
    /// </summary>
    public required IReadOnlyDictionary<string, FluxOptionKind> EncodeOptions { get; init; }

    /// <summary>
    /// Encode options a manifest must never set, with the reason given to the user.
    /// </summary>
    public IReadOnlyDictionary<string, string> RefusedOptions { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Resolves <c>${NAME}</c>; <see langword="null"/> means unset.
    /// </summary>
    public Func<string, string?> Variables { get; init; } = _ => null;

    /// <summary>
    /// Whether outputs may resolve outside the manifest's directory. Off by default: a Fluxfile from somewhere else
    /// should not be able to write over files elsewhere on the machine.
    /// </summary>
    public bool AllowOutputsOutside { get; init; }
}

/// <summary>
/// Turns a parsed manifest into a validated <see cref="Fluxfile"/> (TSKE011.1–TSKE011.3), reporting every problem it
/// finds, each with its line and column.
/// </summary>
/// <remarks>
/// <para>Format, version 1 (schema: <c>docs/schema/fluxfile-v1.schema.json</c>):</para>
/// <code>
/// version: 1                 # required
/// defaults:                  # optional: encode options for every job
///   codec: hevc
/// jobs:                      # required, at least one
///   master:                  # job name
///     input: raw/take1.mov   # or a list; "@job" means that job's outputs
///     output: out/master.mkv # file or directory (trailing /); default: beside the input
///     preset: archive        # a preset from settings.json
///     needs: [other]         # optional ordering; "@job" inputs imply it
///     quality: high          # any encode option, as on the command line without dashes
/// </code>
/// <para>Rules: unknown keys are errors (typos must not be silently ignored); job values override <c>defaults</c>;
/// <c>${NAME}</c> is substituted in every value; relative paths resolve against the manifest's directory; two jobs
/// may not name the same output file; <c>needs</c> must name existing jobs and form no cycle.</para>
/// </remarks>
public static class FluxfileReader
{
    private const string VersionKey = "version";
    private const string DefaultsKey = "defaults";
    private const string JobsKey = "jobs";
    private const string SchemaKey = "$schema";

    private static readonly string[] TopLevelKeys = [SchemaKey, VersionKey, DefaultsKey, JobsKey];
    private static readonly string[] JobKeys = ["input", "output", "needs", "preset", "description"];

    public static (Fluxfile? File, ImmutableArray<ManifestError> Errors) Read(ManifestNode root, FluxfileReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(options);

        var reader = new State(options);
        var file = reader.ReadRoot(root);
        return reader.Errors.Count == 0 ? (file, []) : (null, reader.Errors.ToImmutable());
    }

    private sealed class State(FluxfileReadOptions options)
    {
        private readonly string _directory = Path.GetDirectoryName(Path.GetFullPath(options.Path))!;

        public ImmutableArray<ManifestError>.Builder Errors { get; } = ImmutableArray.CreateBuilder<ManifestError>();

        private void Error(ManifestNode? at, string message) => Errors.Add(new ManifestError(at?.Position, message));

        public Fluxfile? ReadRoot(ManifestNode root)
        {
            if (root is not ManifestMapping mapping)
            {
                Error(root, "A Fluxfile must be a mapping with 'version' and 'jobs'.");
                return null;
            }

            var version = 0;
            var defaults = ImmutableArray<FluxOption>.Empty;
            ManifestNode? jobsNode = null;

            foreach (var (key, value) in mapping.Entries)
            {
                switch (key.Value)
                {
                    case SchemaKey:
                        break;
                    case VersionKey:
                        version = ReadVersion(value);
                        break;
                    case DefaultsKey:
                        defaults = value is ManifestMapping d ? ReadOptions(d, []) : Invalid(value, "'defaults' must be a mapping of encode options.");
                        break;
                    case JobsKey:
                        jobsNode = value;
                        break;
                    default:
                        Error(key, Unknown(key.Value, "top-level key", TopLevelKeys));
                        break;
                }
            }

            if (!mapping.Entries.Any(e => e.Key.Value == VersionKey))
            {
                Error(root, $"'version' is required; this FluxEncoder reads version {Fluxfile.CurrentVersion}.");
            }

            var jobs = ReadJobs(jobsNode, root, defaults);
            ValidateGraph(jobs);
            ValidateOutputs(jobs);

            return new Fluxfile(Path.GetFullPath(options.Path), version, jobs);
        }

        private int ReadVersion(ManifestNode node)
        {
            if (node is ManifestScalar { IsNull: false } scalar && int.TryParse(scalar.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version))
            {
                if (version == Fluxfile.CurrentVersion)
                {
                    return version;
                }

                Error(node, version > Fluxfile.CurrentVersion
                    ? $"Fluxfile version {version} is newer than this FluxEncoder understands ({Fluxfile.CurrentVersion}); update FluxEncoder."
                    : $"Fluxfile version {version} is not supported; use version {Fluxfile.CurrentVersion}.");
                return version;
            }

            Error(node, "'version' must be a whole number, such as 1.");
            return 0;
        }

        private ImmutableArray<FluxJob> ReadJobs(ManifestNode? node, ManifestNode root, ImmutableArray<FluxOption> defaults)
        {
            if (node is null)
            {
                Error(root, "'jobs' is required: a Fluxfile with no jobs would do nothing.");
                return [];
            }

            if (node is not ManifestMapping mapping)
            {
                Error(node, "'jobs' must be a mapping of job names to jobs.");
                return [];
            }

            if (mapping.Entries.IsEmpty)
            {
                Error(node, "'jobs' is empty: a Fluxfile with no jobs would do nothing.");
                return [];
            }

            var jobs = ImmutableArray.CreateBuilder<FluxJob>();
            foreach (var (name, value) in mapping.Entries)
            {
                if (!IsValidJobName(name.Value))
                {
                    Error(name, $"Job name '{name.Value}' is not valid; use letters, digits, '.', '_', and '-', starting with a letter or digit.");
                    continue;
                }

                if (jobs.Any(j => string.Equals(j.Name, name.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    Error(name, $"Job names are case-insensitive; '{name.Value}' is already used.");
                    continue;
                }

                if (value is not ManifestMapping job)
                {
                    Error(value, $"Job '{name.Value}' must be a mapping with at least 'input'.");
                    continue;
                }

                jobs.Add(ReadJob(name.Value, job, defaults));
            }

            return jobs.ToImmutable();
        }

        private FluxJob ReadJob(string name, ManifestMapping job, ImmutableArray<FluxOption> defaults)
        {
            var inputs = ImmutableArray<FluxInput>.Empty;
            string? output = null;
            string? preset = null;
            var needs = new List<string>();

            foreach (var (key, value) in job.Entries)
            {
                switch (key.Value)
                {
                    case "input":
                        inputs = ReadInputs(value);
                        break;
                    case "output":
                        output = Text(value, "'output'") is { } o ? ResolveOutput(value, o) : null;
                        break;
                    case "needs":
                        needs.AddRange(Strings(value, "'needs'"));
                        break;
                    case "preset":
                        preset = Text(value, "'preset'");
                        break;
                    case "description":
                        Text(value, "'description'");
                        break;
                }
            }

            if (!job.Entries.Any(e => e.Key.Value == "input"))
            {
                Error(job, $"Job '{name}' has no 'input'.");
            }

            foreach (var reference in inputs.Where(i => i.FromJob is not null))
            {
                if (!needs.Contains(reference.FromJob!, StringComparer.OrdinalIgnoreCase))
                {
                    needs.Add(reference.FromJob!);
                }
            }

            // Job options override defaults; options keep their first position so commands read predictably.
            var own = ReadOptions(job, JobKeys);
            // A flag set to false only cancels a default; it never reaches the command.
            var merged = defaults
                .Where(d => !own.Any(o => o.Name == d.Name))
                .Concat(own)
                .Where(o => !(o.Value == "false" && options.EncodeOptions.GetValueOrDefault(o.Name) == FluxOptionKind.Flag));

            return new FluxJob(name, inputs, output, [.. needs], preset, [.. merged], job.Position);
        }

        private ImmutableArray<FluxInput> ReadInputs(ManifestNode node)
        {
            var inputs = ImmutableArray.CreateBuilder<FluxInput>();
            foreach (var text in Strings(node, "'input'"))
            {
                if (text.StartsWith('@'))
                {
                    inputs.Add(FluxInput.Outputs(text[1..]));
                    continue;
                }

                var source = InputSource.Classify(text);
                inputs.Add(FluxInput.Source(source is { Kind: InputKind.LocalFile, Scheme: null } ? Resolve(text) : text));
            }

            if (inputs.Count == 0)
            {
                Error(node, "'input' needs at least one path, URL, or @job.");
            }

            return inputs.ToImmutable();
        }

        private string? ResolveOutput(ManifestNode at, string text)
        {
            if (InputSource.Classify(text) is not { Kind: InputKind.LocalFile, Scheme: null })
            {
                Error(at, $"'output' must be a local file or directory, not '{text}'.");
                return null;
            }

            var full = Resolve(text);
            if (!options.AllowOutputsOutside && !IsInside(full, _directory))
            {
                Error(at, $"'output' resolves to '{full}', outside the Fluxfile's directory '{_directory}'. Pass --allow-outside to permit it.");
                return null;
            }

            return full;
        }

        /// <summary>
        /// Absolute, against the manifest's directory. A trailing separator is kept: it marks a directory output.
        /// </summary>
        private string Resolve(string path)
        {
            var full = Path.GetFullPath(Path.Combine(_directory, path));
            return Path.EndsInDirectorySeparator(path) && !Path.EndsInDirectorySeparator(full) ? full + Path.DirectorySeparatorChar : full;
        }

        private static bool IsInside(string path, string directory)
        {
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var root = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
            return path.StartsWith(root, comparison) && path.Length > root.Length;
        }

        private ImmutableArray<FluxOption> ReadOptions(ManifestMapping mapping, string[] reserved)
        {
            var result = ImmutableArray.CreateBuilder<FluxOption>();
            foreach (var (key, value) in mapping.Entries)
            {
                if (reserved.Contains(key.Value))
                {
                    continue;
                }

                if (options.RefusedOptions.TryGetValue(key.Value, out var reason))
                {
                    Error(key, $"'{key.Value}' cannot be set in a Fluxfile: {reason}");
                    continue;
                }

                if (!options.EncodeOptions.TryGetValue(key.Value, out var kind))
                {
                    var known = reserved.Length == 0 ? options.EncodeOptions.Keys : options.EncodeOptions.Keys.Concat(reserved);
                    Error(key, Unknown(key.Value, reserved.Length == 0 ? "encode option" : "job key or encode option", known));
                    continue;
                }

                if (kind == FluxOptionKind.Flag)
                {
                    switch (Text(value, $"'{key.Value}'"))
                    {
                        case "true":
                            result.Add(new FluxOption(key.Value, null));
                            break;
                        case "false":
                            result.Add(new FluxOption(key.Value, "false"));
                            break;
                        case { } other:
                            Error(value, $"'{key.Value}' takes true or false, not '{other}'.");
                            break;
                    }

                    continue;
                }

                var values = Strings(value, $"'{key.Value}'");
                if (values.Count > 0)
                {
                    result.Add(new FluxOption(key.Value, string.Join(',', values)));
                }
            }

            return result.ToImmutable();
        }

        private void ValidateGraph(ImmutableArray<FluxJob> jobs)
        {
            var names = jobs.Select(j => j.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var valid = true;
            foreach (var job in jobs)
            {
                foreach (var need in job.Needs.Where(n => !names.Contains(n)))
                {
                    Errors.Add(new ManifestError(job.Position, $"Job '{job.Name}' needs '{need}', which is not a job here."));
                    valid = false;
                }

                if (job.Needs.Contains(job.Name, StringComparer.OrdinalIgnoreCase))
                {
                    Errors.Add(new ManifestError(job.Position, $"Job '{job.Name}' cannot need itself."));
                    valid = false;
                }
            }

            if (!valid)
            {
                return;
            }

            var byName = jobs.ToDictionary(j => j.Name, StringComparer.OrdinalIgnoreCase);
            var state = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var path = new List<string>();

            bool Visit(string name)
            {
                if (state.TryGetValue(name, out var finished))
                {
                    if (!finished)
                    {
                        var cycle = path.SkipWhile(p => !string.Equals(p, name, StringComparison.OrdinalIgnoreCase)).Append(name);
                        Errors.Add(new ManifestError(byName[name].Position, $"Jobs depend on each other in a cycle: {string.Join(" → ", cycle)}."));
                        return false;
                    }

                    return true;
                }

                state[name] = false;
                path.Add(name);
                var ok = byName[name].Needs.All(Visit);
                path.RemoveAt(path.Count - 1);
                state[name] = true;
                return ok;
            }

            foreach (var job in jobs)
            {
                if (!Visit(job.Name))
                {
                    return;
                }
            }
        }

        private void ValidateOutputs(ImmutableArray<FluxJob> jobs)
        {
            var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var owners = new Dictionary<string, string>(comparer);

            foreach (var job in jobs)
            {
                if (job.Output is not { } output || Path.EndsInDirectorySeparator(output))
                {
                    continue;
                }

                if (owners.TryGetValue(output, out var other))
                {
                    Errors.Add(new ManifestError(job.Position, $"Jobs '{other}' and '{job.Name}' both write '{output}'."));
                }
                else
                {
                    owners[output] = job.Name;
                }

                if (job.Inputs.Any(i => i.Path is { } input && comparer.Equals(input, output)))
                {
                    Errors.Add(new ManifestError(job.Position, $"Job '{job.Name}' would write over its own input '{output}'."));
                }
            }
        }

        private string? Text(ManifestNode node, string what)
        {
            if (node is not ManifestScalar scalar)
            {
                Error(node, $"{what} must be a single value.");
                return null;
            }

            if (scalar.IsNull || scalar.Value.Length == 0)
            {
                Error(node, $"{what} has no value.");
                return null;
            }

            if (!ManifestVariables.TryExpand(scalar.Value, options.Variables, out var expanded, out var error))
            {
                Error(node, error!);
                return null;
            }

            return expanded;
        }

        private List<string> Strings(ManifestNode node, string what)
        {
            if (node is ManifestSequence sequence)
            {
                return [.. sequence.Items.Select(i => Text(i, $"Each item of {what}")).OfType<string>()];
            }

            return Text(node, what) is { } single ? [single] : [];
        }

        private ImmutableArray<FluxOption> Invalid(ManifestNode node, string message)
        {
            Error(node, message);
            return [];
        }
    }

    private static bool IsValidJobName(string name) =>
        name.Length is > 0 and <= 64
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static string Unknown(string key, string what, IEnumerable<string> known)
    {
        var suggestion = known
            .Select(k => (Key: k, Distance: Distance(key, k)))
            .Where(k => k.Distance <= Math.Max(1, key.Length / 3))
            .OrderBy(k => k.Distance)
            .Select(k => k.Key)
            .FirstOrDefault();

        return suggestion is null ? $"Unknown {what} '{key}'." : $"Unknown {what} '{key}'; did you mean '{suggestion}'?";
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
