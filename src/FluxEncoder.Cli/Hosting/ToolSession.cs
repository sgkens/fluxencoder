using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using FluxEncoder.Infrastructure.Execution;
using FluxEncoder.Infrastructure.Planning;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Cli.Hosting;

/// <summary>
/// Locates FFmpeg and FFprobe and reads the build's capabilities once per command run.
/// </summary>
internal static class ToolSession
{
    public static string ScratchRoot => Path.Combine(Path.GetTempPath(), "fluxencoder");

    public static async Task<Result<EncodeEnvironment>> CreateAsync(string? ffmpeg, string? ffprobe, CancellationToken cancellationToken)
    {
        var discovery = FFmpegLocator.Locate(new ToolPathOverrides(ffmpeg, ffprobe), ToolEnvironment.Current);
        if (discovery.ToFailure() is { } missing)
        {
            return missing;
        }

        var capabilities = await FFmpegCapabilityProbe.ProbeAsync(discovery.FFmpeg!.Path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!capabilities.IsSuccess)
        {
            return capabilities.Failure;
        }

        return Result<EncodeEnvironment>.Success(new EncodeEnvironment
        {
            FFmpegPath = discovery.FFmpeg.Path,
            FFprobePath = discovery.FFprobe!.Path,
            Capabilities = capabilities.Value,
            FileSystem = LocalPlanningFileSystem.Instance,
            ScratchRoot = ScratchRoot,
        });
    }
}
