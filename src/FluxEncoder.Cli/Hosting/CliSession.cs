using FluxEncoder.Infrastructure.Configuration;

namespace FluxEncoder.Cli.Hosting;

/// <summary>
/// State shared between argument preparation and command handlers within one invocation.
/// </summary>
internal sealed class CliSession
{
    public FluxSettings Settings { get; set; } = FluxSettings.Default;

    /// <summary>
    /// Problems found in <c>settings.json</c> or a preset, shown before the command runs.
    /// </summary>
    public List<string> Notices { get; } = [];
}
