namespace FluxEncoder.Core.Encoding;

public enum StreamHandling
{
    /// <summary>
    /// Pass the stream through unchanged.
    /// </summary>
    Copy,

    /// <summary>
    /// Re-encode with the job's <see cref="VideoTarget"/> or <see cref="AudioTarget"/>.
    /// </summary>
    Transcode,
}
