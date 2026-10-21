using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Security;
using FluxEncoder.Core.Tooling;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// Everything outside the job that affects its plan. Planning is deterministic for a given context, which is what
/// makes argument snapshots testable.
/// </summary>
public sealed record PlanningContext
{
    public required FFmpegCapabilities Capabilities { get; init; }

    public required IHardwareStrategy Hardware { get; init; }

    public required IPlanningFileSystem FileSystem { get; init; }

    /// <summary>
    /// Directory under which per-job scratch directories (pass logs) are created.
    /// </summary>
    public required string ScratchRoot { get; init; }

    /// <summary>
    /// The platform null output for discarded passes: <c>NUL</c> on Windows, <c>/dev/null</c> elsewhere.
    /// </summary>
    public string NullDevice { get; init; } = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    /// <summary>
    /// Produces the per-job token used in temporary and scratch names. Tests supply a fixed value.
    /// </summary>
    public Func<string> NewToken { get; init; } = () => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Full output paths already claimed by earlier items of the same batch.
    /// </summary>
    public IReadOnlySet<string> ReservedOutputs { get; init; } = new HashSet<string>();

    /// <summary>
    /// Which inputs and locations are allowed, and how inputs are opened (TSKE010.2).
    /// </summary>
    public SourcePolicy Policy { get; init; } = SourcePolicy.Default;

    public ResourceLimits Limits { get; init; } = ResourceLimits.None;
}
