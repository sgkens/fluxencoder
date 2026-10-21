using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// The probed input a job is planned against.
/// </summary>
/// <param name="IsProbed">
/// <see langword="false"/> for pipes: <paramref name="Media"/> then has no streams and FFmpeg's own stream
/// selection applies.
/// </param>
public sealed record PlanningInput(InputSource Source, MediaInfo Media, bool IsProbed = true);
