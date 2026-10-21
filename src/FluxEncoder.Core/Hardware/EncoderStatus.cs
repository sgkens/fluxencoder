namespace FluxEncoder.Core.Hardware;

public enum EncoderStatus
{
    /// <summary>
    /// The FFmpeg build does not include the encoder.
    /// </summary>
    NotListed,

    /// <summary>
    /// The build includes the encoder but a test encode failed: no device, no driver, or the device cannot encode
    /// this codec.
    /// </summary>
    Unavailable,

    /// <summary>
    /// A test encode on the device succeeded.
    /// </summary>
    Available,
}
