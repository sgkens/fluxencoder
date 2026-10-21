namespace FluxEncoder.Core.Encoding;

/// <summary>
/// What to do when the output path already exists. Whatever the policy, an output that resolves to the
/// input itself is always rejected (<c>FailureCategory.OutputIsInput</c>).
/// </summary>
public enum OverwritePolicy
{
    /// <summary>
    /// Fail the job with <c>FailureCategory.OutputExists</c>. The default: no flag can destroy a file by accident.
    /// </summary>
    Never,

    /// <summary>
    /// Leave the existing file and report the job as skipped. Suited to re-running an interrupted batch.
    /// </summary>
    Skip,

    /// <summary>
    /// Write beside it as <c>name (1).ext</c>, <c>name (2).ext</c>, and so on.
    /// </summary>
    Rename,

    /// <summary>
    /// Replace the existing file, only after the new output has been written and validated.
    /// </summary>
    Overwrite,
}
