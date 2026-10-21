using System.Diagnostics;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Infrastructure.History;

/// <summary>
/// Collects progress samples for one run with bounded memory (TSKE009.5).
/// </summary>
/// <remarks>
/// A sample is kept at most once per interval (initially one second). When <see cref="Capacity"/> samples are held,
/// every other one is discarded and the interval doubles, so any run length fits in the same memory and the samples
/// stay evenly spread. The samples are written in one batch when the run ends.
/// </remarks>
public sealed class TelemetryRecorder : IProgress<JobProgress>
{
    public const int DefaultCapacity = 1800;

    private readonly object _gate = new();
    private readonly List<TelemetrySample> _samples = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _interval;
    private TimeSpan _nextSample;

    public TelemetryRecorder(int capacity = DefaultCapacity, TimeSpan? interval = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        Capacity = capacity;
        _interval = interval ?? TimeSpan.FromSeconds(1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_interval, TimeSpan.Zero, nameof(interval));
    }

    public int Capacity { get; }

    public TimeSpan Interval
    {
        get
        {
            lock (_gate)
            {
                return _interval;
            }
        }
    }

    public void Report(JobProgress value) => Add(value.Current, _clock.Elapsed);

    /// <summary>
    /// Adds a report observed at <paramref name="elapsed"/> since the run started. Exposed for deterministic tests.
    /// </summary>
    public void Add(EncodeProgress progress, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(progress);

        lock (_gate)
        {
            if (elapsed < _nextSample && !progress.IsFinal)
            {
                return;
            }

            _samples.Add(new TelemetrySample(
                Math.Round(elapsed.TotalSeconds, 3),
                progress.ProcessedTime?.TotalSeconds,
                progress.FramesPerSecond,
                progress.Speed,
                progress.BitRateKbps,
                progress.TotalSizeBytes,
                progress.CpuPercent is { } cpu ? Math.Round(cpu, 1) : null,
                progress.MemoryBytes,
                progress.DroppedFrames));

            if (_samples.Count >= Capacity)
            {
                for (var i = _samples.Count - 1; i > 0; i -= 2)
                {
                    _samples.RemoveAt(i - 1);
                }

                _interval *= 2;
            }

            _nextSample = elapsed + _interval;
        }
    }

    public IReadOnlyList<TelemetrySample> Samples
    {
        get
        {
            lock (_gate)
            {
                return [.. _samples];
            }
        }
    }
}
