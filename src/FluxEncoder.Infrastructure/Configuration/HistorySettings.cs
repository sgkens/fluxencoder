namespace FluxEncoder.Infrastructure.Configuration;

/// <summary>
/// What the local run history keeps. History never leaves the machine.
/// </summary>
/// <param name="Enabled">Record runs at all.</param>
/// <param name="Telemetry">Also record progress samples (speed, bit rate, CPU, memory) for each run.</param>
/// <param name="RetentionDays">Runs older than this are deleted.</param>
/// <param name="MaxRuns">At most this many runs are kept; the oldest go first.</param>
public sealed record HistorySettings(bool Enabled = true, bool Telemetry = true, int RetentionDays = 90, int MaxRuns = 1000)
{
    public static HistorySettings Default { get; } = new();
}
