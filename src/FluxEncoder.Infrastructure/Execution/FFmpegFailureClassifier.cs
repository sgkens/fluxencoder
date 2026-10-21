using FluxEncoder.Core.Results;

namespace FluxEncoder.Infrastructure.Execution;

/// <summary>
/// Maps a failed FFmpeg exit to a <see cref="FailureCategory"/> by recognizing FFmpeg's error messages.
/// </summary>
/// <remarks>
/// Matching is on stable fragments of FFmpeg's own messages, checked most specific first. Anything not
/// recognized is <see cref="FailureCategory.EncodeFailed"/>, never a thrown exception. The user-facing message
/// is the last error-looking stderr line, so it names the actual cause.
/// </remarks>
public static class FFmpegFailureClassifier
{
    private static readonly (FailureCategory Category, string[] Markers)[] Rules =
    [
        (FailureCategory.InsufficientSpace, ["No space left on device", "Disk quota exceeded"]),
        (FailureCategory.OutputExists, ["already exists. Exiting", "Not overwriting - exiting"]),
        (FailureCategory.HardwareUnavailable,
        [
            "Cannot load libcuda", "Cannot load nvcuda", "No NVENC capable devices", "OpenEncodeSessionEx failed",
            "CUDA_ERROR", "Failed to initialise VAAPI", "No VA display found", "Error creating a MFX session",
            "Failed to create a VAAPI device", "Device creation failed", "No device available for decoder",
            "AMF failed", "DLL amfrt64.dll failed to open", "VTCompressionSessionCreate failed",
            "CreateComponent(AMFVideoEncoder", "10-bit encoder is not supported by AMD GPU drivers",
        ]),
        (FailureCategory.Unsupported,
        [
            "Unknown encoder", "Encoder not found", "Unknown decoder", "Unrecognized option", "Option not found",
            "Requested output format", "is not a suitable output format", "codec not currently supported in container",
            "Could not find tag for codec", "Unsupported codec", "experimental codecs are not enabled",
            "Filter not found", "No such filter",
        ]),
        (FailureCategory.NetworkError,
        [
            "Connection refused", "Connection reset", "Connection timed out", "Network is unreachable",
            "No route to host", "Name or service not known", "Temporary failure in name resolution", "Broken pipe",
        ]),
        (FailureCategory.OutputNotWritable,
        [
            "Error opening output file", "Could not open file", "Read-only file system", "Error opening output files",
        ]),
        (FailureCategory.InvalidInput,
        [
            "Invalid data found when processing input", "moov atom not found", "Error opening input file",
            "could not find codec parameters", "Error while decoding stream",
        ]),
    ];

    private static readonly string[] ErrorWords = ["Error", "error", "failed", "Invalid", "Unknown", "Cannot", "not found", "No such", "denied"];

    /// <summary>
    /// FFmpeg 8 exits with code 0 when <c>-n</c> refuses to overwrite an existing output, and the Windows 7.1 build
    /// exits with code 0 when AMF refuses a 10-bit encode. These messages mean no output was produced whatever the
    /// exit code says.
    /// </summary>
    private static readonly string[] FailureDespiteZeroExit = ["already exists. Exiting", "Error opening output file", "Error while opening encoder"];

    /// <summary>
    /// Whether stderr shows a failure even though FFmpeg exited with code 0.
    /// </summary>
    public static bool IndicatesFailure(string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);
        return FailureDespiteZeroExit.Any(marker => standardError.Contains(marker, StringComparison.Ordinal));
    }

    public static JobFailure Classify(int exitCode, string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);

        var category = FailureCategory.EncodeFailed;
        string? cause = null;

        foreach (var (candidate, markers) in Rules)
        {
            var marker = markers.FirstOrDefault(m => standardError.Contains(m, StringComparison.Ordinal));
            if (marker is not null)
            {
                category = candidate;
                cause = Lines(standardError).FirstOrDefault(line => line.Contains(marker, StringComparison.Ordinal));
                break;
            }
        }

        // "Permission denied" names the file it concerns: the output when opening output failed, else the input.
        if (category == FailureCategory.EncodeFailed && standardError.Contains("Permission denied", StringComparison.Ordinal))
        {
            category = standardError.Contains("output", StringComparison.OrdinalIgnoreCase) ? FailureCategory.OutputNotWritable : FailureCategory.InvalidInput;
        }

        cause ??= LastErrorLine(standardError);
        var message = cause is null
            ? $"FFmpeg exited with code {exitCode}."
            : $"FFmpeg exited with code {exitCode}: {cause}";

        return new JobFailure(category, message) { Detail = standardError.Trim() };
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    private static string? LastErrorLine(string standardError) =>
        Lines(standardError).LastOrDefault(line => ErrorWords.Any(word => line.Contains(word, StringComparison.Ordinal)))
        ?? Lines(standardError).LastOrDefault();
}
