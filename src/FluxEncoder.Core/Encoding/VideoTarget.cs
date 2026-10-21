namespace FluxEncoder.Core.Encoding;

/// <summary>
/// What an encoding job does with the input's video: copy it or re-encode it.
/// The set of cases is closed; match on the nested records. Which streams are kept at all is decided by
/// <see cref="StreamSelection"/>, not here.
/// </summary>
public abstract record VideoTarget
{
    private protected VideoTarget()
    {
    }

    /// <summary>
    /// Copy the video bitstream unchanged (<c>-c:v copy</c>).
    /// </summary>
    public sealed record StreamCopy : VideoTarget;

    public sealed record Encode : VideoTarget
    {
        public Encode(VideoCodec codec, QualityPreset quality)
        {
            Codec = Enum.IsDefined(codec) ? codec : throw new ArgumentOutOfRangeException(nameof(codec), codec, null);
            Quality = Enum.IsDefined(quality) ? quality : throw new ArgumentOutOfRangeException(nameof(quality), quality, null);
        }

        public VideoCodec Codec { get; }

        public QualityPreset Quality { get; }
    }
}
