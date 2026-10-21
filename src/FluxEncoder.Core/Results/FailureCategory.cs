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

    /// <summary>
    /// A hardware encoder or decoder is compiled in but its device or driver is not usable. Automatic hardware
    /// selection may fall back to the CPU on this category (TSKE006.3).
    /// </summary>
    HardwareUnavailable,
    EncodeFailed,
    OutputValidationFailed,

    /// <summary>
    /// FFmpeg stopped making progress for longer than the configured limit. Retryable.
    /// </summary>
    Timeout,

    Cancelled,

    /// <summary>
    /// The source policy forbids this input, protocol, or location (TSKE010.2).
    /// </summary>
    PolicyDenied,
}
