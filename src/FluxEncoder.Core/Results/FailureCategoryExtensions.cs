namespace FluxEncoder.Core.Results;

public static class FailureCategoryExtensions
{
    public static ExitCode ToExitCode(this FailureCategory category) => category switch
    {
        FailureCategory.Internal => ExitCode.GeneralFailure,
        FailureCategory.InvalidUsage => ExitCode.InvalidUsage,
        FailureCategory.InputNotFound or FailureCategory.InvalidInput or FailureCategory.NetworkError
            => ExitCode.InputError,
        FailureCategory.OutputExists or FailureCategory.OutputNotWritable or FailureCategory.InsufficientSpace
            or FailureCategory.OutputIsInput => ExitCode.OutputError,
        FailureCategory.DependencyMissing => ExitCode.DependencyMissing,
        FailureCategory.Unsupported or FailureCategory.HardwareUnavailable => ExitCode.Unsupported,
        FailureCategory.EncodeFailed => ExitCode.EncodeFailed,
        FailureCategory.OutputValidationFailed => ExitCode.OutputValidationFailed,
        FailureCategory.Timeout => ExitCode.Timeout,
        FailureCategory.Cancelled => ExitCode.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>
    /// Whether running the same job again can succeed without the user changing anything.
    /// Only these categories are eligible for automatic retry (see <c>RetryPolicy</c>).
    /// </summary>
    public static bool IsTransient(this FailureCategory category) =>
        category is FailureCategory.NetworkError or FailureCategory.Timeout;
}
