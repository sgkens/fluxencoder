namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Progress of a whole job, which may span several FFmpeg passes.
/// </summary>
/// <param name="Pass">The running pass, starting at 1.</param>
/// <param name="PassCount">Total passes in the plan.</param>
/// <param name="Current">The latest report from the running pass.</param>
/// <param name="Fraction">
/// Overall completion in [0, 1] across all passes, or <see langword="null"/> for unbounded inputs.
/// </param>
/// <param name="Attempt">The attempt number, greater than 1 after a transient-failure retry.</param>
public sealed record JobProgress(int Pass, int PassCount, EncodeProgress Current, double? Fraction, int Attempt = 1);
