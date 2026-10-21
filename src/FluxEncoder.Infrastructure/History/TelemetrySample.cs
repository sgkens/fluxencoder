namespace FluxEncoder.Infrastructure.History;

/// <summary>
/// One measurement during a run. <see langword="null"/> means the value was not measured, never zero
/// (see <c>docs/data-and-telemetry.md</c> for definitions).
/// </summary>
/// <param name="ElapsedSeconds">Wall-clock seconds since the run started.</param>
/// <param name="ProcessedSeconds">Media time processed so far.</param>
/// <param name="FramesPerSecond">Frames encoded per wall-clock second, as FFmpeg reports it.</param>
/// <param name="Speed">Processing speed relative to playback (2.0 = twice real time).</param>
/// <param name="BitRateKbps">Average output bit rate so far, in kbit/s.</param>
/// <param name="SizeBytes">Output bytes written so far.</param>
/// <param name="CpuPercent">FFmpeg's CPU use as a share of the whole machine (all cores), 0–100.</param>
/// <param name="MemoryBytes">FFmpeg's resident memory (working set).</param>
/// <param name="DroppedFrames">Frames FFmpeg dropped to keep the output frame rate.</param>
public sealed record TelemetrySample(
    double ElapsedSeconds,
    double? ProcessedSeconds,
    double? FramesPerSecond,
    double? Speed,
    double? BitRateKbps,
    long? SizeBytes,
    double? CpuPercent,
    long? MemoryBytes,
    long? DroppedFrames);
