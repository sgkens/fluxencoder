namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Encoder-independent speed/quality trade-off. Each hardware strategy maps it to its encoder's own
/// controls (CRF, CQ, QP, preset) under TSKE006.4.
/// </summary>
public enum QualityPreset
{
    Fast,
    Balanced,
    High,
}
