namespace FluxEncoder.Core.Results;

/// <summary>
/// Released <see cref="JobWarning.Code"/> values. Codes are part of the JSON contract: add new ones, never
/// rename or reuse them.
/// </summary>
public static class WarningCodes
{
    /// <summary>
    /// An unbounded input was stopped by the user and the output was finalized.
    /// </summary>
    public const string StoppedByUser = "stopped-by-user";
}
