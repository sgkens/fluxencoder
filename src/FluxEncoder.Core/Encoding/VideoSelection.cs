namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Which motion-video streams to keep. Cover art is controlled separately by
/// <see cref="StreamSelection.CoverArt"/>.
/// </summary>
public enum VideoSelection
{
    /// <summary>
    /// The stream flagged default, else the first. The default: extra angles are rare and usually unwanted.
    /// </summary>
    Primary,

    All,

    /// <summary>
    /// No video, for audio extraction.
    /// </summary>
    None,
}
