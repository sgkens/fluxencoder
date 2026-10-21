using System.ComponentModel;
using System.Globalization;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;
using FluxEncoder.Infrastructure.Processes;

namespace FluxEncoder.Infrastructure.Probing;

/// <summary>
/// Probes an input with FFprobe, classifies failures, and validates the result.
/// </summary>
/// <remarks>
/// Pipes are not probed, because FFprobe would consume data the encode needs. Network inputs get an I/O
/// timeout so an unreachable host fails promptly, and their connection errors are classified as
/// <see cref="FailureCategory.NetworkError"/> so the retry policy can act on them.
/// </remarks>
public static class MediaProber
{
    public static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan NetworkIoTimeout = TimeSpan.FromSeconds(15);

    private const string PqTransfer = "smpte2084";

    private static readonly string[] NetworkErrorMarkers =
    [
        "Connection refused",
        "Connection reset",
        "Connection timed out",
        "timed out",
        "Network is unreachable",
        "No route to host",
        "Name or service not known",
        "Temporary failure in name resolution",
        "Could not resolve",
        "Failed to resolve",
        "Server returned 5",
        "End of file",
        "I/O error",
    ];

    public static async Task<Result<ProbedInput>> ProbeAsync(
        string ffprobePath,
        string input,
        CancellationToken cancellationToken = default,
        SourcePolicy? policy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var source = InputSource.Classify(input);
        if ((policy ?? SourcePolicy.Default).CheckInput(source, Path.GetFullPath) is { } denied)
        {
            return denied;
        }

        if (!source.CanProbe)
        {
            return Result<ProbedInput>.Success(new ProbedInput(
                source,
                new MediaInfo { Source = input },
                IsProbed: false,
                [new JobWarning(WarningCodes.InputNotProbed, "Pipe input is not probed; FFmpeg chooses the streams and progress has no percentage.")]));
        }

        if (source.LocalPath is { } path && CheckLocalFile(path) is { } missing)
        {
            return missing;
        }

        var timeout = source.Kind == InputKind.Network ? NetworkTimeout : LocalTimeout;
        ProcessOutput output;

        try
        {
            output = await ProcessCapture.RunAsync(ffprobePath, MediaArguments(source), timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            return new JobFailure(FailureCategory.DependencyMissing, $"FFprobe at '{ffprobePath}' could not be started.") { Detail = ex.Message };
        }

        if (!output.Succeeded)
        {
            return Classify(source, output, timeout);
        }

        MediaInfo media;
        try
        {
            media = FFprobeJsonParser.Parse(output.StandardOutput, input);
        }
        catch (FormatException ex)
        {
            return new JobFailure(FailureCategory.InvalidInput, $"FFprobe returned unreadable output for '{input}'.")
            {
                Detail = output.OutputTruncated ? "Output exceeded the capture limit." : ex.Message,
            };
        }

        if (source.IsSeekable)
        {
            media = await AddFrameHdrMetadataAsync(ffprobePath, source, media, cancellationToken).ConfigureAwait(false);
        }

        var (failure, warnings) = ProbeValidator.Validate(media, source);
        return failure ?? Result<ProbedInput>.Success(new ProbedInput(source, media, IsProbed: true, warnings));
    }

    public static IReadOnlyList<string> MediaArguments(InputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<string> arguments = ["-v", "error", "-hide_banner", "-print_format", "json", "-show_format", "-show_streams", "-show_chapters"];

        if (source.Kind == InputKind.Network)
        {
            var microseconds = (long)NetworkIoTimeout.TotalMicroseconds;
            arguments.AddRange(["-rw_timeout", microseconds.ToString(CultureInfo.InvariantCulture)]);
        }

        // -i keeps an input that starts with '-' from being read as an option; the policy adds the protocol
        // whitelist and passes local files as absolute paths so their names are never read as protocols.
        arguments.AddRange(SourcePolicy.FFmpegInput(source, Path.GetFullPath));
        return arguments;
    }

    private static JobFailure? CheckLocalFile(string path)
    {
        if (Directory.Exists(path))
        {
            return new JobFailure(FailureCategory.InvalidInput, $"'{path}' is a directory, not a media file.");
        }

        return File.Exists(path) ? null : new JobFailure(FailureCategory.InputNotFound, $"Input '{path}' does not exist.");
    }

    private static JobFailure Classify(InputSource source, ProcessOutput output, TimeSpan timeout)
    {
        var stderr = output.StandardError.Trim();

        if (output.TimedOut)
        {
            return source.Kind == InputKind.Network
                ? new JobFailure(FailureCategory.NetworkError, $"Timed out after {timeout.TotalSeconds:0} s reading '{source.Value}'.") { Detail = stderr }
                : new JobFailure(FailureCategory.Timeout, $"FFprobe did not finish reading '{source.Value}' within {timeout.TotalSeconds:0} s.") { Detail = stderr };
        }

        if (source.Kind == InputKind.Network)
        {
            if (stderr.Contains("Server returned 4", StringComparison.Ordinal))
            {
                return new JobFailure(FailureCategory.InputNotFound, $"The server rejected '{source.Value}'.") { Detail = stderr };
            }

            if (NetworkErrorMarkers.Any(marker => stderr.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return new JobFailure(FailureCategory.NetworkError, $"Could not read '{source.Value}' over the network.") { Detail = stderr };
            }
        }

        if (stderr.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
        {
            return new JobFailure(FailureCategory.InputNotFound, $"Input '{source.Value}' does not exist.") { Detail = stderr };
        }

        if (stderr.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return new JobFailure(FailureCategory.InvalidInput, $"Input '{source.Value}' cannot be read: permission denied.") { Detail = stderr };
        }

        return new JobFailure(FailureCategory.InvalidInput, $"'{source.Value}' is not a readable media file or is damaged.") { Detail = stderr };
    }

    /// <summary>
    /// MP4 and raw HEVC carry HDR10 mastering metadata in frames, not stream headers. For a PQ video stream
    /// without it, read the first frame. Failure here is not fatal: the stream is still HDR10, only the
    /// metadata to preserve is missing.
    /// </summary>
    private static async Task<MediaInfo> AddFrameHdrMetadataAsync(string ffprobePath, InputSource source, MediaInfo media, CancellationToken cancellationToken)
    {
        var target = media.MotionVideoStreams.FirstOrDefault(v =>
            string.Equals(v.Color.Transfer, PqTransfer, StringComparison.OrdinalIgnoreCase) && v.Hdr.MasteringDisplay is null);

        if (target is null)
        {
            return media;
        }

        ProcessOutput output;
        try
        {
            output = await ProcessCapture.RunAsync(
                ffprobePath,
                ["-v", "error", "-hide_banner", "-print_format", "json", "-select_streams", target.Index.ToString(CultureInfo.InvariantCulture),
                 "-show_frames", "-read_intervals", "%+#1", "-show_entries", "frame=side_data_list", .. SourcePolicy.FFmpegInput(source, Path.GetFullPath)],
                LocalTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return media;
        }

        if (!output.Succeeded)
        {
            return media;
        }

        HdrInfo hdr;
        try
        {
            hdr = FFprobeJsonParser.MergeFrameSideData(target.Hdr, output.StandardOutput);
        }
        catch (FormatException)
        {
            return media;
        }

        return media with { Streams = [.. media.Streams.Select(s => ReferenceEquals(s, target) ? target with { Hdr = hdr } : s)] };
    }
}
