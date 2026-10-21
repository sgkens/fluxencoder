namespace FluxEncoder.Core.Results;

/// <summary>
/// Why a job failed. Finer-grained than <see cref="ExitCode"/>; several categories share one exit code.
/// </summary>
public enum FailureCategory
{
    Internal,
    InvalidUsage,
    InputNotFound,
    InvalidInput,

    /// <summary>
    /// A network input could not be reached or dropped mid-read. Retryable.
    /// </summary>
    NetworkError,

    OutputExists,
    OutputNotWritable,
    InsufficientSpace,

    /// <summary>
    /// The resolved output path is the input path. Never permitted, whatever the overwrite policy.
    /// </summary>
    OutputIsInput,

    DependencyMissing,
    Unsupported,
    EncodeFailed,
    OutputValidationFailed,

    /// <summary>
    /// FFmpeg stopped making progress for longer than the configured limit. Retryable.
    /// </summary>
    Timeout,

    Cancelled,
}
