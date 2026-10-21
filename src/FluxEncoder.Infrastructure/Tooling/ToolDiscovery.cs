using System.Collections.Immutable;
using System.Text;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Infrastructure.Tooling;

/// <summary>
/// Outcome of <see cref="FFmpegLocator.Locate"/>: the resolved tools plus every location tried, so a failure
/// can tell the user exactly where FluxEncoder looked.
/// </summary>
public sealed record ToolDiscovery(
    ToolLocation? FFmpeg,
    ToolLocation? FFprobe,
    ImmutableArray<string> Searched,
    ImmutableArray<string> Problems,
    HostPlatform Platform)
{
    public bool IsComplete => FFmpeg is not null && FFprobe is not null;

    /// <summary>
    /// A <see cref="FailureCategory.DependencyMissing"/> failure describing what is missing and how to fix it,
    /// or <see langword="null"/> when both tools were found.
    /// </summary>
    public JobFailure? ToFailure()
    {
        if (IsComplete)
        {
            return null;
        }

        var missing = (FFmpeg, FFprobe) switch
        {
            (null, null) => "FFmpeg and FFprobe were not found.",
            (null, _) => "FFmpeg was not found.",
            _ => "FFprobe was not found.",
        };

        var detail = new StringBuilder();
        foreach (var problem in Problems)
        {
            detail.AppendLine(problem);
        }

        if (Searched.Length > 0)
        {
            detail.AppendLine("Searched:");
            foreach (var path in Searched)
            {
                detail.Append("  ").AppendLine(path);
            }
        }

        detail.Append(InstallHint(Platform));
        detail.AppendLine();
        detail.Append("Or point FluxEncoder at an existing install with --ffmpeg <path> or FLUXENCODER_FFMPEG.");

        return new JobFailure(FailureCategory.DependencyMissing, missing) { Detail = detail.ToString() };
    }

    private static string InstallHint(HostPlatform platform) => platform switch
    {
        HostPlatform.Windows => "Install FFmpeg with: winget install Gyan.FFmpeg",
        HostPlatform.MacOS => "Install FFmpeg with: brew install ffmpeg",
        _ => "Install FFmpeg with your package manager, for example: sudo apt install ffmpeg",
    };
}
