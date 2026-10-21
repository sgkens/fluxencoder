using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// One output stream after container fitting: its source, its FFmpeg output specifier, and how it is written.
/// </summary>
/// <param name="Source">The input stream mapped with <c>-map 0:{Source.Index}</c>.</param>
/// <param name="Type">FFmpeg stream type letter: <c>v</c>, <c>a</c>, <c>s</c>, <c>t</c>, or <c>d</c>.</param>
/// <param name="TypeIndex">Position among output streams of the same type, used in specifiers like <c>-c:v:1</c>.</param>
/// <param name="Codec">The encoder, or <c>copy</c>.</param>
/// <param name="Options">Per-stream options (name without dash, value); the planner adds the stream specifier.</param>
/// <param name="Disposition">The output disposition.</param>
/// <param name="IsMotionVideo">A transcoded or copied main video stream, as opposed to cover art.</param>
public sealed record FittedStream(
    MediaStream Source,
    char Type,
    int TypeIndex,
    string Codec,
    IReadOnlyList<KeyValuePair<string, string>> Options,
    StreamDisposition Disposition,
    bool IsMotionVideo = false)
{
    public bool IsCopy => Codec == "copy";

    public string Specifier => $"{Type}:{TypeIndex}";
}
