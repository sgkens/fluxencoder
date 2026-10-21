namespace FluxEncoder.Infrastructure.Tooling;

/// <summary>
/// Where a tool was found, in search priority order.
/// </summary>
public enum ToolSource
{
    /// <summary>
    /// A path given by command-line flag or configuration file.
    /// </summary>
    Configured,

    /// <summary>
    /// <c>FLUXENCODER_FFMPEG</c> or <c>FLUXENCODER_FFPROBE</c>.
    /// </summary>
    EnvironmentVariable,

    /// <summary>
    /// The directory of the other, already resolved tool, which keeps FFmpeg and FFprobe from the same build.
    /// </summary>
    Sibling,

    /// <summary>
    /// Shipped next to the FluxEncoder executable.
    /// </summary>
    ApplicationDirectory,

    SearchPath,

    /// <summary>
    /// A common install location not on <c>PATH</c>, such as Homebrew or WinGet links.
    /// </summary>
    WellKnownLocation,
}
