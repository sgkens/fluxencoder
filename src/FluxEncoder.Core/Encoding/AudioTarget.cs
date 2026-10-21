namespace FluxEncoder.Core.Encoding;

/// <summary>
/// What an encoding job does with the input's audio: copy it or re-encode it.
/// The set of cases is closed; match on the nested records. Which streams are kept at all is decided by
/// <see cref="StreamSelection"/>, not here.
/// </summary>
public abstract record AudioTarget
{
    private protected AudioTarget()
    {
    }

    /// <summary>
    /// Copy the audio bitstream unchanged (<c>-c:a copy</c>).
    /// </summary>
    public sealed record StreamCopy : AudioTarget;

    public sealed record Encode : AudioTarget
    {
        /// <param name="bitRateKbps">
        /// Target bit rate in kbit/s, or <see langword="null"/> for the encoder default. Ignored for lossless codecs.
        /// </param>
        public Encode(AudioCodec codec, int? bitRateKbps = null)
        {
            Codec = Enum.IsDefined(codec) ? codec : throw new ArgumentOutOfRangeException(nameof(codec), codec, null);

            if (bitRateKbps is { } rate)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate, nameof(bitRateKbps));
            }

            BitRateKbps = bitRateKbps;
        }

        public AudioCodec Codec { get; }

        public int? BitRateKbps { get; }
    }
}
