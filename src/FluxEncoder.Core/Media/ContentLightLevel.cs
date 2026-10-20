namespace FluxEncoder.Core.Media;

/// <summary>
/// CTA-861.3 content light level, in cd/m².
/// </summary>
/// <param name="MaxContent">MaxCLL.</param>
/// <param name="MaxAverage">MaxFALL.</param>
public sealed record ContentLightLevel(int MaxContent, int MaxAverage);
