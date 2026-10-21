namespace FluxEncoder.Core.Media;

/// <summary>
/// SMPTE ST 2086 mastering display colour volume. Chromaticity coordinates are CIE 1931 xy values and
/// luminance is in cd/m², all as the exact ratios FFprobe reports.
/// </summary>
public sealed record MasteringDisplay
{
    public required Rational RedX { get; init; }

    public required Rational RedY { get; init; }

    public required Rational GreenX { get; init; }

    public required Rational GreenY { get; init; }

    public required Rational BlueX { get; init; }

    public required Rational BlueY { get; init; }

    public required Rational WhitePointX { get; init; }

    public required Rational WhitePointY { get; init; }

    public required Rational MinLuminance { get; init; }

    public required Rational MaxLuminance { get; init; }
}
