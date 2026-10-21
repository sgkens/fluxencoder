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

        /// <summary>
        /// Average bit rate in kbit/s. When set, the encode targets a size instead of a constant quality.
        /// </summary>
        public int? TargetBitRateKbps
        {
            get;
            init
            {
                if (value is { } rate)
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate);
                }

                field = value;
            }
        }

        /// <summary>
        /// Analyse the whole input first so a target bit rate is spent where it matters. Requires
        /// <see cref="TargetBitRateKbps"/>; constant-quality encodes gain nothing from a second pass.
        /// </summary>
        public bool TwoPass { get; init; }
    }
}
