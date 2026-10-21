namespace FluxEncoder.Core.Media;

public enum InputKind
{
    LocalFile,

    /// <summary>
    /// A URL read through an FFmpeg network protocol (HTTP, RTMP, RTSP, SRT, UDP, and so on).
    /// </summary>
    Network,

    /// <summary>
    /// Standard input or a file descriptor. Data can be read once, so it cannot be probed ahead of the encode.
    /// </summary>
    Pipe,

    /// <summary>
    /// A capture device such as <c>/dev/video0</c>.
    /// </summary>
    Device,
}
