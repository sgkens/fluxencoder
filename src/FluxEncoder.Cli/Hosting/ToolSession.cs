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

    public static ToolDiscovery Locate(CliHost? host, ToolPathOverrides settingsTools, string? ffmpeg, string? ffprobe)
    {
        var overrides = new ToolPathOverrides(ffmpeg ?? settingsTools.FFmpeg, ffprobe ?? settingsTools.FFprobe);
        var environment = host is null
            ? ToolEnvironment.Current
            : ToolEnvironment.Current with { GetVariable = name => host.GetVariable(name) ?? ToolEnvironment.Current.GetVariable(name) };
        return FFmpegLocator.Locate(overrides, environment);
    }

    public static async Task<Result<EncodeEnvironment>> CreateAsync(
        string? ffmpeg,
        string? ffprobe,
        CancellationToken cancellationToken,
        ToolPathOverrides? settingsTools = null,
        CliHost? host = null)
    {
        var discovery = Locate(host, settingsTools ?? ToolPathOverrides.None, ffmpeg, ffprobe);
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
