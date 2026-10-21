namespace FluxEncoder.Core.Media;

/// <summary>
/// HDR signalling of a video stream: the transfer-derived format plus static and dynamic metadata.
/// </summary>
public sealed record HdrInfo
{
    private const string PqTransfer = "smpte2084";
    private const string HlgTransfer = "arib-std-b67";

    public MasteringDisplay? MasteringDisplay { get; init; }

    public ContentLightLevel? ContentLightLevel { get; init; }

    public bool HasHdr10PlusMetadata { get; init; }

    public bool HasDolbyVisionConfiguration { get; init; }

    public static HdrInfo None { get; } = new();

    /// <summary>
    /// Combines the stream's transfer characteristic with its HDR side data.
    /// </summary>
    /// <remarks>
    /// Static metadata without a PQ transfer is reported as no format: it is signalling only and does not
    /// make an SDR transfer HDR.
    /// </remarks>
    public HdrFormat GetFormats(ColorInfo color)
    {
        ArgumentNullException.ThrowIfNull(color);

        var formats = HdrFormat.None;

        if (string.Equals(color.Transfer, PqTransfer, StringComparison.OrdinalIgnoreCase))
        {
            formats |= HdrFormat.Hdr10;

            if (HasHdr10PlusMetadata)
            {
                formats |= HdrFormat.Hdr10Plus;
            }
        }
        else if (string.Equals(color.Transfer, HlgTransfer, StringComparison.OrdinalIgnoreCase))
        {
            formats |= HdrFormat.Hlg;
        }

        if (HasDolbyVisionConfiguration)
        {
            formats |= HdrFormat.DolbyVision;
        }

        return formats;
    }
}
