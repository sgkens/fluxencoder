namespace FluxEncoder.Core.Media;

/// <summary>
/// A non-presentational stream such as timecode (<c>tmcd</c>) or GPS telemetry.
/// </summary>
public sealed record DataStream : MediaStream
{
    public override MediaStreamKind Kind => MediaStreamKind.Data;
}
