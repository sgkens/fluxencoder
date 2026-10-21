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
}
