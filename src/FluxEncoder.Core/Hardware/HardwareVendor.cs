namespace FluxEncoder.Core.Hardware;

/// <summary>
/// Encoder family a hardware strategy targets. <see cref="Cpu"/> is the software encoders.
/// </summary>
public enum HardwareVendor
{
    Cpu,
    Nvidia,
    Amd,
    Intel,
    Apple,
}
