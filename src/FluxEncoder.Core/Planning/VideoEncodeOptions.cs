using System.Globalization;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// Options that carry a source video's picture properties through a re-encode (TSKE005.9).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Bit depth: HEVC, AV1, and VVC keep 10-bit sources (and anything HDR) at 10-bit when the encoder and device
/// can; H.264 is written 8-bit for playback compatibility. Reductions are warned about. Chroma is written 4:2:0.
/// Pixel formats come from the strategy (CPU encoders take yuv420p/yuv420p10le, GPUs nv12/p010le).</item>
/// <item>Colour range, primaries, transfer, and matrix are passed explicitly when known, because encoders do not
/// all copy them from the decoded frames.</item>
/// <item>HDR10 mastering display and content light level are written into libx265's SEI. Dolby Vision and HDR10+
/// dynamic metadata cannot survive a re-encode and are warned about.</item>
/// <item>Variable frame rate sources are encoded with <c>-fps_mode passthrough</c> so no frames are duplicated or
/// dropped.</item>
/// <item>Rotation: FFmpeg applies the display matrix to the pixels when re-encoding (autorotate) and drops it,
/// which is the intended transform; stream copies keep the matrix unchanged. Sample aspect ratio is kept by FFmpeg
/// when no scaling is applied.</item>
/// </list>
/// </remarks>
public static class VideoEncodeOptions
{
    public static IReadOnlyList<KeyValuePair<string, string>> For(
        VideoStream source, VideoCodec codec, string encoder, IHardwareStrategy strategy, ICollection<JobWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(warnings);

        var options = new List<KeyValuePair<string, string>>();
        var hdr = source.HdrFormats;
        var deep = (source.BitDepth ?? 8) > 8;

        if (codec == VideoCodec.H264)
        {
            if (hdr != HdrFormat.None)
            {
                warnings.Add(new JobWarning(WarningCodes.HdrInH264, $"Stream {source.Index} is HDR; 8-bit H.264 will band and few players handle HDR H.264. Use HEVC or AV1 to keep HDR."));
            }
            else if (deep)
            {
                warnings.Add(new JobWarning(WarningCodes.BitDepthReduced, $"Stream {source.Index} is {source.BitDepth}-bit; H.264 output is 8-bit."));
            }

            options.Add(new("pix_fmt", strategy.GetPixelFormat(codec, highBitDepth: false)!));
        }
        else
        {
            var wantsHigh = deep || hdr != HdrFormat.None;
            var format = strategy.GetPixelFormat(codec, wantsHigh);

            if (format is null)
            {
                format = strategy.GetPixelFormat(codec, highBitDepth: false)!;
                warnings.Add(hdr != HdrFormat.None
                    ? new JobWarning(WarningCodes.HdrNotPreserved, $"{strategy.DisplayName} cannot encode 10-bit on this machine; HDR stream {source.Index} is written 8-bit and will band. Use --hardware cpu to keep HDR.")
                    : new JobWarning(WarningCodes.BitDepthReduced, $"{strategy.DisplayName} cannot encode 10-bit on this machine; stream {source.Index} is written 8-bit."));
            }
            else if (hdr.HasFlag(HdrFormat.Hdr10) && strategy.Vendor != HardwareVendor.Cpu)
            {
                warnings.Add(new JobWarning(WarningCodes.HardwareHdrMetadata, $"{strategy.DisplayName} may not write HDR10 mastering metadata for stream {source.Index}; --hardware cpu guarantees it."));
            }

            options.Add(new("pix_fmt", format));
        }

        AddIfKnown(options, "color_range", source.Color.Range);
        AddIfKnown(options, "color_primaries", source.Color.Primaries);
        AddIfKnown(options, "color_trc", source.Color.Transfer);
        AddIfKnown(options, "colorspace", source.Color.Matrix);

        if (encoder == "libx265" && hdr.HasFlag(HdrFormat.Hdr10) && X265HdrParameters(source.Hdr) is { } x265)
        {
            options.Add(new("x265-params", x265));
        }

        if (hdr.HasFlag(HdrFormat.DolbyVision))
        {
            warnings.Add(new JobWarning(WarningCodes.DolbyVisionLost, $"Stream {source.Index}: re-encoding removes Dolby Vision."));
        }

        if (hdr.HasFlag(HdrFormat.Hdr10Plus))
        {
            warnings.Add(new JobWarning(WarningCodes.Hdr10PlusLost, $"Stream {source.Index}: re-encoding removes HDR10+ dynamic metadata."));
        }

        if (IsVariableFrameRate(source))
        {
            options.Add(new("fps_mode", "passthrough"));
        }

        return options;
    }

    /// <summary>
    /// The source's average and base frame rates disagree, which is how FFprobe exposes variable frame rate.
    /// </summary>
    public static bool IsVariableFrameRate(VideoStream source) =>
        source.AverageFrameRate is { } average && source.BaseFrameRate is { } baseRate && average != baseRate;

    /// <summary>
    /// x265's <c>master-display</c> uses chromaticity in units of 0.00002 and luminance in 0.0001 cd/m².
    /// </summary>
    public static string? X265HdrParameters(HdrInfo hdr)
    {
        ArgumentNullException.ThrowIfNull(hdr);

        var parts = new List<string> { "hdr10=1", "repeat-headers=1" };

        if (hdr.MasteringDisplay is { } m)
        {
            static long Chroma(Rational value) => (long)Math.Round(value.Value * 50000);
            static long Luminance(Rational value) => (long)Math.Round(value.Value * 10000);

            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"master-display=G({Chroma(m.GreenX)},{Chroma(m.GreenY)})B({Chroma(m.BlueX)},{Chroma(m.BlueY)})R({Chroma(m.RedX)},{Chroma(m.RedY)})WP({Chroma(m.WhitePointX)},{Chroma(m.WhitePointY)})L({Luminance(m.MaxLuminance)},{Luminance(m.MinLuminance)})"));
        }

        if (hdr.ContentLightLevel is { } cll)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"max-cll={cll.MaxContent},{cll.MaxAverage}"));
        }

        return string.Join(':', parts);
    }

    private static void AddIfKnown(List<KeyValuePair<string, string>> options, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value) && value != "unknown")
        {
            options.Add(new(name, value));
        }
    }
}
