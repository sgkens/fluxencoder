using System.Collections.Immutable;

namespace FluxEncoder.Core.Manifests;

/// <summary>
/// A validated Fluxfile: encode jobs with their resolved inputs, outputs, and options (TSKE011.1). Defaults are
/// already merged into each job and variables already substituted.
/// </summary>
/// <param name="Path">The manifest's full path.</param>
/// <param name="Jobs">Jobs in file order. Run them in <see cref="ExecutionOrder"/>, which respects <c>needs</c>.</param>
public sealed record Fluxfile(string Path, int Version, ImmutableArray<FluxJob> Jobs)
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// Jobs ordered so each runs after everything it needs; otherwise file order is kept. With
    /// <paramref name="selected"/>, only those jobs and everything they need, directly or not.
    /// </summary>
    /// <exception cref="ArgumentException">A selected name is not a job.</exception>
    public ImmutableArray<FluxJob> ExecutionOrder(IReadOnlyCollection<string>? selected = null)
    {
        var byName = Jobs.ToDictionary(j => j.Name, StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Want(string name)
        {
            if (wanted.Add(name))
            {
                foreach (var need in byName[name].Needs)
                {
                    Want(need);
                }
            }
        }

        foreach (var name in selected is { Count: > 0 } ? selected : byName.Keys)
        {
            if (!byName.ContainsKey(name))
            {
                throw new ArgumentException($"There is no job '{name}' (jobs: {string.Join(", ", Jobs.Select(j => j.Name))}).", nameof(selected));
            }

            Want(name);
        }

        // The reader has already refused cycles, so repeatedly taking the first ready job always finishes.
        var order = ImmutableArray.CreateBuilder<FluxJob>(wanted.Count);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = Jobs.Where(j => wanted.Contains(j.Name)).ToList();
        while (pending.Count > 0)
        {
            var ready = pending.First(j => j.Needs.All(done.Contains));
            order.Add(ready);
            done.Add(ready.Name);
            pending.Remove(ready);
        }

        return order.MoveToImmutable();
    }
}

/// <param name="Name">Unique, case-insensitive: letters, digits, <c>.</c>, <c>_</c>, and <c>-</c>.</param>
/// <param name="Inputs">Files, directories, URLs, or <c>remote://</c> paths; local paths are absolute.</param>
/// <param name="Output">An absolute file or directory path, or <see langword="null"/> to write beside each input.</param>
/// <param name="Needs">Jobs that must succeed (or be skipped with their output in place) before this one starts.</param>
/// <param name="Options">Encode options, with names as on the command line but without the dashes.</param>
public sealed record FluxJob(
    string Name,
    ImmutableArray<FluxInput> Inputs,
    string? Output,
    ImmutableArray<string> Needs,
    string? Preset,
    ImmutableArray<FluxOption> Options,
    ManifestPosition Position);

/// <summary>
/// One input: a path or URL, or the outputs of another job (<c>@name</c> in the file).
/// </summary>
public sealed record FluxInput(string? Path, string? FromJob)
{
    public static FluxInput Source(string path) => new(path, null);

    public static FluxInput Outputs(string job) => new(null, job);

    public override string ToString() => FromJob is null ? Path! : "@" + FromJob;
}

/// <param name="Value"><see langword="null"/> for a flag that is set.</param>
public sealed record FluxOption(string Name, string? Value);
