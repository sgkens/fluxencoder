namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Whether a batch keeps going after an item fails. Cancellation always stops the batch.
/// </summary>
public enum BatchFailurePolicy
{
    /// <summary>
    /// Run every item and report failures in the summary. The default: one bad file does not waste an
    /// unattended overnight batch.
    /// </summary>
    Continue,

    /// <summary>
    /// Stop at the first failure; remaining items are reported as not started.
    /// </summary>
    Stop,
}
