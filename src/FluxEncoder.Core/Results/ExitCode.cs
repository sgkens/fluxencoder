namespace FluxEncoder.Core.Results;

/// <summary>
/// Process exit codes. The numeric values are a public contract for scripts and must never be renumbered;
/// add new codes only at unused values.
/// </summary>
/// <remarks>
/// Codes group failures by what the user can do about them rather than by internal cause. Warnings never
/// change the exit code. <see cref="Cancelled"/> follows the shell convention of 128 + SIGINT so wrappers
/// treat it like any interrupted command, on every platform.
/// </remarks>
public enum ExitCode
{
    Success = 0,

    /// <summary>
    /// An unexpected internal error, or a batch whose items failed for different reasons.
    /// </summary>
    GeneralFailure = 1,

    /// <summary>
    /// Invalid command line or configuration. Nothing was attempted.
    /// </summary>
    InvalidUsage = 2,

    /// <summary>
    /// An input is missing, unreadable, unreachable, or not valid media.
    /// </summary>
    InputError = 3,

    /// <summary>
    /// The output exists and the overwrite policy forbids replacing it, or it cannot be written.
    /// </summary>
    OutputError = 4,

    /// <summary>
    /// FFmpeg or FFprobe could not be found or started.
    /// </summary>
    DependencyMissing = 5,

    /// <summary>
    /// The requested codec, container, hardware, or option combination is not available.
    /// </summary>
    Unsupported = 6,

    /// <summary>
    /// FFmpeg ran and failed.
    /// </summary>
    EncodeFailed = 7,

    /// <summary>
    /// FFmpeg reported success but the output did not pass validation.
    /// </summary>
    OutputValidationFailed = 8,

    Timeout = 9,

    /// <summary>
    /// A batch where some items succeeded or were skipped and others failed.
    /// </summary>
    PartialFailure = 10,

    Cancelled = 130,
}
