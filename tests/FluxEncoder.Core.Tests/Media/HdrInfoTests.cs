using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Tests.Media;

public sealed class HdrInfoTests
{
    private static readonly ColorInfo Pq = new() { Transfer = "smpte2084", Primaries = "bt2020" };
    private static readonly ColorInfo Hlg = new() { Transfer = "arib-std-b67", Primaries = "bt2020" };
    private static readonly ColorInfo Sdr = new() { Transfer = "bt709", Primaries = "bt709" };

    [Fact]
    public void PqTransfer_IsHdr10()
    {
        Assert.Equal(HdrFormat.Hdr10, HdrInfo.None.GetFormats(Pq));
    }

    [Fact]
    public void PqWithDynamicMetadata_IsHdr10AndHdr10Plus()
    {
        var hdr = new HdrInfo { HasHdr10PlusMetadata = true };

        Assert.Equal(HdrFormat.Hdr10 | HdrFormat.Hdr10Plus, hdr.GetFormats(Pq));
    }

    [Fact]
    public void HlgTransfer_IsHlg()
    {
        Assert.Equal(HdrFormat.Hlg, HdrInfo.None.GetFormats(Hlg));
    }

    [Fact]
    public void DolbyVision_CombinesWithBaseLayer()
    {
        var hdr = new HdrInfo { HasDolbyVisionConfiguration = true };

        Assert.Equal(HdrFormat.Hdr10 | HdrFormat.DolbyVision, hdr.GetFormats(Pq));
    }

    [Fact]
    public void SdrTransfer_IgnoresStaticAndDynamicMetadata()
    {
        var hdr = new HdrInfo
        {
            ContentLightLevel = new ContentLightLevel(1000, 400),
            HasHdr10PlusMetadata = true,
        };

        Assert.Equal(HdrFormat.None, hdr.GetFormats(Sdr));
        Assert.Equal(HdrFormat.None, hdr.GetFormats(ColorInfo.Unspecified));
    }

    [Fact]
    public void VideoStream_ExposesDerivedFormats()
    {
        var stream = new VideoStream { Index = 0, Color = Hlg };

        Assert.Equal(HdrFormat.Hlg, stream.HdrFormats);
    }
}
