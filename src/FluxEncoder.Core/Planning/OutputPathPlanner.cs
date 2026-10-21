using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// Resolves where a job writes and rejects destinations that cannot or must not be written.
/// </summary>
/// <remarks>
/// Rules, in order: the output must be a local file with a supported extension; it must never be the input
/// (whatever the overwrite policy); its directory must be writable (missing directories are created at run time,
/// but a path through an existing file is rejected); an existing file or another batch item's output is handled
/// by <see cref="OverwritePolicy"/>; and the volume must have room.
/// </remarks>
public static class OutputPathPlanner
{
    /// <summary>
    /// Below this, failure is certain for any real encode, so the job is rejected rather than warned about.
    /// </summary>
    public const long MinimumFreeBytes = 64L * 1024 * 1024;

    public sealed record Decision(string? FinalPath, string? SkipReason, JobFailure? Failure, IReadOnlyList<JobWarning> Warnings);

    public static Decision Resolve(EncodingJob job, PlanningInput input, PlanningContext context, ContainerProfile profile, double copiedFraction)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(input);

        return Resolve(job.OutputPath, job.Policy, [input.Source], (long)((input.Media.SizeBytes ?? 0) * copiedFraction), context);
    }

    /// <param name="inputs">Every input of the job; the output may be none of them.</param>
    /// <param name="estimatedBytes">Expected output size, for the low-space warning; 0 when unknown.</param>
    public static Decision Resolve(string outputPath, JobPolicy policy, IReadOnlyList<InputSource> inputs, long estimatedBytes, PlanningContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(context);

        var fs = context.FileSystem;
        var warnings = new List<JobWarning>();

        if (InputSource.Classify(outputPath).Kind != InputKind.LocalFile)
        {
            return Fail(FailureCategory.InvalidUsage, $"Output '{outputPath}' is not a file path; streaming and pipe outputs are not supported yet.");
        }

        var requested = fs.GetFullPath(outputPath);
        if (IsInput(requested, inputs, fs))
        {
            return Fail(FailureCategory.OutputIsInput, $"The output '{outputPath}' is an input file. Choose a different output name.");
        }

        var directory = Path.GetDirectoryName(requested) ?? requested;
        var existing = NearestExistingDirectory(directory, fs, out var blockedBy);
        if (blockedBy is not null)
        {
            return Fail(FailureCategory.OutputNotWritable, $"Cannot create '{directory}': '{blockedBy}' is a file.");
        }

        if (existing is null || !fs.IsWritableDirectory(existing))
        {
            return Fail(FailureCategory.OutputNotWritable, $"'{existing ?? directory}' is not writable.");
        }

        bool Reserved(string path) => context.ReservedOutputs.Contains(path);
        bool Taken(string path) => Reserved(path) || fs.FileExists(path) || IsInput(path, inputs, fs);

        var final = requested;
        if (Taken(requested))
        {
            switch (policy.Overwrite)
            {
                case OverwritePolicy.Skip when !Reserved(requested):
                    return new Decision(requested, $"'{requested}' already exists.", null, warnings);

                case OverwritePolicy.Rename:
                    final = OutputPaths.FirstAvailable(requested, Taken)
                        ?? throw new InvalidOperationException("No free numbered name.");
                    warnings.Add(new JobWarning(WarningCodes.OutputRenamed, $"'{requested}' exists; writing '{final}' instead."));
                    break;

                case OverwritePolicy.Overwrite when !Reserved(requested):
                    break;

                default:
                    return Reserved(requested)
                        ? Fail(FailureCategory.OutputExists, $"Another item in this batch already writes '{requested}'.")
                        : Fail(FailureCategory.OutputExists, $"'{requested}' already exists. Use --overwrite, --skip-existing, or --rename-existing.");
            }
        }

        if (fs.AvailableFreeSpace(existing) is { } free)
        {
            if (free < MinimumFreeBytes)
            {
                return Fail(FailureCategory.InsufficientSpace, $"Only {Megabytes(free)} MB free on the output volume.");
            }

            if (estimatedBytes > free)
            {
                warnings.Add(new JobWarning(
                    WarningCodes.LowDiskSpace,
                    $"About {Megabytes(estimatedBytes)} MB may be needed but only {Megabytes(free)} MB is free."));
            }
        }

        return new Decision(final, null, null, warnings);

        Decision Fail(FailureCategory category, string message) => new(null, null, new JobFailure(category, message), warnings);
    }

    private static bool IsInput(string fullOutput, IReadOnlyList<InputSource> inputs, IPlanningFileSystem fs) =>
        inputs.Any(input => input.LocalPath is { } inputPath && fs.IsSameFile(fs.GetFullPath(inputPath), fullOutput));

    private static string? NearestExistingDirectory(string directory, IPlanningFileSystem fs, out string? blockedBy)
    {
        blockedBy = null;
        for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (fs.DirectoryExists(current))
            {
                return current;
            }

            if (fs.FileExists(current))
            {
                blockedBy = current;
                return null;
            }
        }

        return null;
    }

    private static long Megabytes(long bytes) => bytes / (1024 * 1024);
}
