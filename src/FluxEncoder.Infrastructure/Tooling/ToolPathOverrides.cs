namespace FluxEncoder.Infrastructure.Tooling;

/// <summary>
/// User-supplied tool locations from flags or configuration. Each value may be the executable itself or the
/// directory holding it. A set value is authoritative: if it is wrong, discovery fails instead of silently
/// using some other FFmpeg.
/// </summary>
public sealed record ToolPathOverrides(string? FFmpeg = null, string? FFprobe = null)
{
    public static ToolPathOverrides None { get; } = new();
}
