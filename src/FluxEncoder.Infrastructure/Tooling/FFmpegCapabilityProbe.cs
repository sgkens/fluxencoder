using System.ComponentModel;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using FluxEncoder.Infrastructure.Processes;

namespace FluxEncoder.Infrastructure.Tooling;

/// <summary>
/// Builds an <see cref="FFmpegCapabilities"/> snapshot by running FFmpeg's listing commands concurrently.
/// </summary>
public static class FFmpegCapabilityProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private static readonly string[] Listings =
        ["-version", "-encoders", "-decoders", "-muxers", "-demuxers", "-protocols", "-filters", "-hwaccels"];

    public static async Task<Result<FFmpegCapabilities>> ProbeAsync(
        string ffmpegPath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);

        var limit = timeout ?? DefaultTimeout;
        ProcessOutput[] outputs;

        try
        {
            outputs = await Task.WhenAll(Listings.Select(listing =>
                ProcessCapture.RunAsync(ffmpegPath, ["-hide_banner", listing], limit, cancellationToken))).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            return new JobFailure(FailureCategory.DependencyMissing, $"FFmpeg at '{ffmpegPath}' could not be started.")
            {
                Detail = ex.Message,
            };
        }

        for (var i = 0; i < outputs.Length; i++)
        {
            if (!outputs[i].Succeeded)
            {
                var reason = outputs[i].TimedOut ? $"timed out after {limit.TotalSeconds:0} s" : $"exited with code {outputs[i].ExitCode}";
                return new JobFailure(FailureCategory.DependencyMissing, $"FFmpeg at '{ffmpegPath}' is not working: 'ffmpeg {Listings[i]}' {reason}.")
                {
                    Detail = outputs[i].StandardError,
                };
            }
        }

        return Parse(
            version: outputs[0].StandardOutput,
            encoders: outputs[1].StandardOutput,
            decoders: outputs[2].StandardOutput,
            muxers: outputs[3].StandardOutput,
            demuxers: outputs[4].StandardOutput,
            protocols: outputs[5].StandardOutput,
            filters: outputs[6].StandardOutput,
            hardwareAccelerators: outputs[7].StandardOutput,
            ffmpegPath);
    }

    /// <summary>
    /// Assembles a snapshot from captured listing text. Separate from <see cref="ProbeAsync"/> so it can be
    /// tested against fixtures without FFmpeg.
    /// </summary>
    public static Result<FFmpegCapabilities> Parse(
        string version,
        string encoders,
        string decoders,
        string muxers,
        string demuxers,
        string protocols,
        string filters,
        string hardwareAccelerators,
        string ffmpegPath)
    {
        if (FFmpegListingParser.ParseVersion(version) is not var (parsedVersion, configuration))
        {
            return new JobFailure(FailureCategory.DependencyMissing, $"'{ffmpegPath}' did not identify itself as FFmpeg.")
            {
                Detail = version.Length > 500 ? version[..500] : version,
            };
        }

        var (input, output) = FFmpegListingParser.ParseProtocols(protocols);

        return Result<FFmpegCapabilities>.Success(new FFmpegCapabilities
        {
            Version = parsedVersion,
            Configuration = configuration,
            Encoders = FFmpegListingParser.ParseCodecs(encoders),
            Decoders = FFmpegListingParser.ParseCodecs(decoders),
            Muxers = FFmpegListingParser.ParseFormats(muxers),
            Demuxers = FFmpegListingParser.ParseFormats(demuxers),
            InputProtocols = input,
            OutputProtocols = output,
            Filters = FFmpegListingParser.ParseFilters(filters),
            HardwareAccelerators = FFmpegListingParser.ParseHardwareAccelerators(hardwareAccelerators),
        });
    }
}
