using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Security;
using FluxEncoder.Core.Tooling;

namespace FluxEncoder.Infrastructure.Execution;

/// <summary>
/// The installed tools and host facts a job runs against.
/// </summary>
public sealed record EncodeEnvironment
{
    public required string FFmpegPath { get; init; }

    public required string FFprobePath { get; init; }

    public required FFmpegCapabilities Capabilities { get; init; }

    public required IPlanningFileSystem FileSystem { get; init; }

    public required string ScratchRoot { get; init; }

    /// <summary>
    /// The strategies to select from; defaults to <see cref="HardwareRegistry.For"/> the capabilities.
    /// </summary>
    public HardwareRegistry Registry
    {
        get => field ??= HardwareRegistry.For(Capabilities);
        init;
    }

    /// <summary>
    /// Outputs claimed by earlier items of the same batch.
    /// </summary>
    public IReadOnlySet<string> ReservedOutputs { get; init; } = new HashSet<string>();

    public FFmpegRunOptions RunOptions { get; init; } = FFmpegRunOptions.Default;

    /// <summary>
    /// Which inputs and locations are allowed (TSKE010.2).
    /// </summary>
    public SourcePolicy Policy { get; init; } = SourcePolicy.Default;

    public PlanningContext PlanningContext(IHardwareStrategy strategy) => new()
    {
        Capabilities = Capabilities,
        Hardware = strategy,
        FileSystem = FileSystem,
        ScratchRoot = ScratchRoot,
        ReservedOutputs = ReservedOutputs,
        Policy = Policy,
    };
}
