namespace FluxEncoder.Core.Media;

/// <summary>
/// Colour description of a video stream, using FFmpeg's names (for example <c>bt709</c>, <c>bt2020nc</c>,
/// <c>smpte2084</c>). A <see langword="null"/> property means the stream does not declare that value.
/// </summary>
/// <remarks>
/// The values stay as FFmpeg strings rather than enums because the planner passes them back to FFmpeg
/// unchanged (TSKE005.9), and FFmpeg's set of names grows between releases.
/// </remarks>
public sealed record ColorInfo
{
    /// <summary>
    /// <c>tv</c> (limited) or <c>pc</c> (full).
    /// </summary>
    public string? Range { get; init; }

    public string? Primaries { get; init; }

    public string? Transfer { get; init; }

    /// <summary>
    /// Matrix coefficients, reported by FFprobe as <c>color_space</c>.
    /// </summary>
    public string? Matrix { get; init; }

    public string? ChromaLocation { get; init; }

    public static ColorInfo Unspecified { get; } = new();
}
