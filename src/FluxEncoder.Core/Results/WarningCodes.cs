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

    /// <summary>
    /// No audio stream matched the requested languages; the default audio stream was kept instead.
    /// </summary>
    public const string AudioLanguageNotFound = "audio-language-not-found";

    /// <summary>
    /// No subtitle stream matched the requested languages; the output has no subtitles.
    /// </summary>
    public const string SubtitleLanguageNotFound = "subtitle-language-not-found";

    /// <summary>
    /// A local file without a usable duration; it is processed like an unbounded input.
    /// </summary>
    public const string DurationUnknown = "duration-unknown";

    /// <summary>
    /// FFmpeg could not identify a stream's codec.
    /// </summary>
    public const string UnknownCodec = "unknown-codec";

    /// <summary>
    /// The input is a pipe and was not probed, so stream selection cannot be applied in advance.
    /// </summary>
    public const string InputNotProbed = "input-not-probed";

    /// <summary>
    /// A failed or cancelled job's temporary output was kept, as requested by the partial-output policy.
    /// </summary>
    public const string PartialOutputKept = "partial-output-kept";

    /// <summary>
    /// A temporary file or scratch directory could not be removed.
    /// </summary>
    public const string CleanupFailed = "cleanup-failed";
}
