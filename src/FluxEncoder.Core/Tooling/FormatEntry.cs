using System.Collections.Immutable;

namespace FluxEncoder.Core.Tooling;

/// <summary>
/// One muxer or demuxer. FFmpeg lists some under several aliases on one line (<c>matroska,webm</c>,
/// <c>mov,mp4,m4a,3gp,3g2,mj2</c>); each alias is a separate entry in <see cref="Names"/>.
/// </summary>
public sealed record FormatEntry
{
    public FormatEntry(IEnumerable<string> names, string description, bool isDevice = false)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(description);

        Names = [.. names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim())];
        if (Names.IsEmpty)
        {
            throw new ArgumentException("A format needs at least one name.", nameof(names));
        }

        Description = description;
        IsDevice = isDevice;
    }

    public ImmutableArray<string> Names { get; }

    /// <summary>
    /// The first listed alias, used when FluxEncoder has to name the format (for example <c>-f matroska</c>).
    /// </summary>
    public string Name => Names[0];

    public string Description { get; }

    /// <summary>
    /// A capture or playback device (<c>alsa</c>, <c>dshow</c>, <c>avfoundation</c>) rather than a file format.
    /// </summary>
    public bool IsDevice { get; }

    public bool Equals(FormatEntry? other) =>
        other is not null && Names.SequenceEqual(other.Names) && Description == other.Description && IsDevice == other.IsDevice;

    public override int GetHashCode() => HashCode.Combine(Name, Description, IsDevice);
}
