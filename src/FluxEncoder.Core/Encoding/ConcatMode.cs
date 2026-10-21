namespace FluxEncoder.Core.Encoding;

public enum ConcatMode
{
    /// <summary>
    /// Copy when the inputs are compatible, otherwise re-encode. The default.
    /// </summary>
    Auto,

    /// <summary>
    /// Copy only; incompatible inputs are an error instead of a slow re-encode.
    /// </summary>
    Copy,

    /// <summary>
    /// Always re-encode, normalizing size, frame rate, and audio format to the first input.
    /// </summary>
    Reencode,
}
