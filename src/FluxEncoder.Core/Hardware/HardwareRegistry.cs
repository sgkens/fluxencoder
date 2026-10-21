using FluxEncoder.Core.Tooling;

namespace FluxEncoder.Core.Hardware;

/// <summary>
/// The single set of encoder strategies (TSKE006.1): the CPU plus the GPU vendors in automatic-selection order.
/// </summary>
/// <remarks>
/// Order for <c>auto</c>: NVIDIA, AMD, Intel, Apple. Discrete GPUs come before integrated ones because a machine
/// with both usually has the discrete card attached to the display and the stronger encoder. Only strategies whose
/// encoder appears in the FFmpeg build are probed, so the order costs nothing on machines without that vendor.
/// </remarks>
public sealed class HardwareRegistry
{
    public HardwareRegistry(CpuStrategy cpu, IEnumerable<IHardwareStrategy> gpus)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(gpus);

        Cpu = cpu;
        Gpus = [.. gpus];

        if (Gpus.Any(g => g.Vendor == HardwareVendor.Cpu) || Gpus.DistinctBy(g => g.Vendor).Count() != Gpus.Count)
        {
            throw new ArgumentException("Each GPU vendor may appear once, and the CPU is not a GPU.", nameof(gpus));
        }
    }

    public CpuStrategy Cpu { get; }

    public IReadOnlyList<IHardwareStrategy> Gpus { get; }

    /// <summary>
    /// The standard registry for an FFmpeg build. AV1 on the CPU uses libsvtav1 when present.
    /// </summary>
    public static HardwareRegistry For(FFmpegCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        return new HardwareRegistry(
            new CpuStrategy(useSvtAv1: capabilities.HasEncoder("libsvtav1")),
            [new NvencStrategy(), new AmfStrategy(), new QsvStrategy(), new VideoToolboxStrategy()]);
    }

    public IHardwareStrategy For(HardwareVendor vendor) =>
        vendor == HardwareVendor.Cpu
            ? Cpu
            : Gpus.FirstOrDefault(g => g.Vendor == vendor)
              ?? throw new ArgumentOutOfRangeException(nameof(vendor), vendor, "No strategy for this vendor.");
}
