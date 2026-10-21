namespace FluxEncoder.Core.Results;

public enum JobOutcome
{
    Succeeded,

    /// <summary>
    /// Not attempted by policy, for example an existing output under <c>OverwritePolicy.Skip</c>.
    /// Counts as success for the exit code.
    /// </summary>
    Skipped,

    Failed,
}
