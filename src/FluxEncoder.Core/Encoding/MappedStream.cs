using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// One output stream: the input stream it comes from, how it is processed, and its output disposition.
/// Its output index is its position in <see cref="StreamMap.Streams"/>.
/// </summary>
public sealed record MappedStream(MediaStream Source, StreamHandling Handling, StreamDisposition Disposition)
{
    public MediaStream Source { get; } = Source ?? throw new ArgumentNullException(nameof(Source));
}
