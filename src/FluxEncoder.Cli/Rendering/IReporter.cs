using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Cli.Rendering;

/// <summary>
/// Presents a run. The human reporter draws progress and a summary; the JSON reporter stays silent until it writes
/// one document to stdout.
/// </summary>
internal interface IReporter
{
    /// <summary>
    /// A problem that stops the run before any job starts: bad usage, missing FFmpeg.
    /// </summary>
    void Fatal(JobFailure failure);

    /// <summary>
    /// A problem with settings or presets that does not stop the run. Always shown, even with <c>--quiet</c>.
    /// </summary>
    void Notice(string message);

    /// <summary>
    /// Runs one job, presenting its progress. Whether a percentage can be shown is learned from the reports
    /// themselves (<see cref="JobProgress.Fraction"/> is <see langword="null"/> for inputs without a known length).
    /// </summary>
    Task<JobReport> RunJobAsync(int index, int count, string label, Func<IProgress<JobProgress>, Task<JobReport>> run);

    /// <summary>
    /// A finished or planned job.
    /// </summary>
    void Completed(JobReport report, bool dryRun, string ffmpegPath);

    void Finished(IReadOnlyList<JobReport> reports, BatchResult batch, bool dryRun);
}
