using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Chooses the encoder strategy for a job (TSKE006.3).
/// </summary>
/// <remarks>
/// <para>Rules:</para>
/// <list type="number">
/// <item>Stream copy needs no encoder: CPU strategy, nothing probed.</item>
/// <item>VVC has no hardware encoders: CPU only; forcing a GPU for VVC is an error.</item>
/// <item><c>--hardware cpu</c>: the CPU.</item>
/// <item><c>--hardware &lt;vendor&gt;</c>: that vendor if its probe succeeded. Otherwise an error, unless
/// <c>--hardware-fallback</c> was given, in which case the CPU with a warning. A device that cannot do 10-bit
/// encodes 8-bit with a warning rather than being rejected, since the user chose it.</item>
/// <item>Automatic (no <c>--hardware</c>): the CPU for the High preset, two-pass, and HDR sources, where CPU
/// encoders give better quality per bit and reliably write HDR metadata. Otherwise the first GPU in registry order
/// whose probe succeeded and which supports the bit depth the source needs; the CPU if none qualifies. Choosing the
/// CPU here is silent: it is the expected outcome on machines without a usable GPU.</item>
/// </list>
/// <para>
/// A hardware encode that fails at run time with <see cref="FailureCategory.HardwareUnavailable"/> (driver crash,
/// session limit) is re-run on the CPU when <see cref="HardwareSelection.FallbackAllowed"/> is set.
/// </para>
/// </remarks>
public static class HardwareSelector
{
    /// <summary>
    /// The GPU strategies whose encoders must be probed before <see cref="Select"/> can decide, limited to those the
    /// FFmpeg build lists.
    /// </summary>
    public static IEnumerable<IHardwareStrategy> StrategiesToProbe(EncodingJob job, MediaInfo media, HardwareRegistry registry, Func<string, bool> isListed)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(isListed);

        if (job.Video is not VideoTarget.Encode encode || encode.Codec == VideoCodec.Vvc || job.Hardware == HardwareVendor.Cpu)
        {
            return [];
        }

        IEnumerable<IHardwareStrategy> candidates = job.Hardware is { } forced
            ? [registry.For(forced)]
            : PrefersCpu(encode, media) ? [] : registry.Gpus;

        return candidates.Where(s => s.TryGetVideoEncoder(encode.Codec, out var encoder) && isListed(encoder));
    }

    public static Result<HardwareSelection> Select(EncodingJob job, MediaInfo media, HardwareRegistry registry, IReadOnlyList<EncoderProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(probes);

        if (job.Video is not VideoTarget.Encode encode || job.Hardware == HardwareVendor.Cpu)
        {
            return Cpu();
        }

        if (encode.Codec == VideoCodec.Vvc)
        {
            return job.Hardware is { } vendor
                ? new JobFailure(FailureCategory.Unsupported, $"VVC has no {vendor} hardware encoder; omit --hardware to encode VVC on the CPU.")
                : Cpu();
        }

        var needsHighBitDepth = NeedsHighBitDepth(encode.Codec, media);

        if (job.Hardware is { } forced)
        {
            var strategy = registry.For(forced);
            var probe = probes.FirstOrDefault(p => p.Vendor == forced && p.Codec == encode.Codec);

            if (probe is { IsAvailable: true })
            {
                var chosen = needsHighBitDepth && !probe.SupportsHighBitDepth ? new EightBitOnlyStrategy(strategy) : strategy;
                return Result<HardwareSelection>.Success(new HardwareSelection(chosen, job.HardwareFallback, []));
            }

            var reason = Reason(strategy, encode.Codec, probe);
            if (job.HardwareFallback)
            {
                return Result<HardwareSelection>.Success(new HardwareSelection(
                    registry.Cpu,
                    FallbackAllowed: false,
                    [new JobWarning(WarningCodes.HardwareFallback, $"{reason} Encoding on the CPU instead.")]));
            }

            return new JobFailure(FailureCategory.HardwareUnavailable, $"{reason} Use --hardware-fallback to encode on the CPU instead.")
            {
                Detail = probe?.Failure?.Detail,
            };
        }

        if (PrefersCpu(encode, media))
        {
            return Cpu();
        }

        foreach (var gpu in registry.Gpus)
        {
            var probe = probes.FirstOrDefault(p => p.Vendor == gpu.Vendor && p.Codec == encode.Codec);
            if (probe is { IsAvailable: true } && (!needsHighBitDepth || probe.SupportsHighBitDepth))
            {
                return Result<HardwareSelection>.Success(new HardwareSelection(gpu, FallbackAllowed: true, []));
            }
        }

        return Cpu();

        Result<HardwareSelection> Cpu() => Result<HardwareSelection>.Success(new HardwareSelection(registry.Cpu, FallbackAllowed: false, []));
    }

    /// <summary>
    /// Automatic selection keeps the CPU where it is clearly better: the High preset, two-pass bit-rate encodes,
    /// and HDR sources (CPU encoders reliably write HDR10 metadata).
    /// </summary>
    public static bool PrefersCpu(VideoTarget.Encode encode, MediaInfo media)
    {
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentNullException.ThrowIfNull(media);

        return encode.Quality == QualityPreset.High
            || encode.TwoPass
            || media.MotionVideoStreams.Any(v => v.HdrFormats != HdrFormat.None);
    }

    private static bool NeedsHighBitDepth(VideoCodec codec, MediaInfo media) =>
        codec != VideoCodec.H264
        && media.MotionVideoStreams.Any(v => v.BitDepth > 8 || v.HdrFormats != HdrFormat.None);

    private static string Reason(IHardwareStrategy strategy, VideoCodec codec, EncoderProbe? probe)
    {
        strategy.TryGetVideoEncoder(codec, out var encoder);
        return probe switch
        {
            null or { Status: EncoderStatus.NotListed } => encoder is null
                ? $"{strategy.DisplayName} has no {codec} encoder."
                : $"This FFmpeg build has no {encoder} encoder.",
            { Failure: { } failure } => $"{strategy.DisplayName} cannot encode {codec} on this machine ({failure.Message}).",
            _ => $"{strategy.DisplayName} cannot encode {codec} on this machine.",
        };
    }
}
