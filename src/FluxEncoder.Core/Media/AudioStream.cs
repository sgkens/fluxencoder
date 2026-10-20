namespace FluxEncoder.Core.Media;

public sealed record AudioStream : MediaStream
{
    public override MediaStreamKind Kind => MediaStreamKind.Audio;

    public int? Channels { get; init; }

    /// <summary>
    /// FFmpeg channel layout name such as <c>stereo</c> or <c>5.1(side)</c>.
    /// </summary>
    public string? ChannelLayout { get; init; }

    public int? SampleRate { get; init; }

    public string? SampleFormat { get; init; }
}
