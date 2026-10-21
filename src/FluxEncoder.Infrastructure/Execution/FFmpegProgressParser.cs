using System.Globalization;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Infrastructure.Execution;

/// <summary>
/// Incremental parser for FFmpeg <c>-progress</c> output: blocks of <c>key=value</c> lines, each closed by
/// <c>progress=continue</c> or <c>progress=end</c>.
/// </summary>
/// <remarks>
/// All numbers are parsed with the invariant culture. <c>out_time_us</c> is preferred; <c>out_time_ms</c> is
/// also microseconds despite its name (a long-standing FFmpeg quirk), and <c>out_time</c> is the fallback.
/// Negative times, which FFmpeg reports before the first output packet, are treated as unknown.
/// </remarks>
public sealed class FFmpegProgressParser
{
    private readonly Dictionary<string, string> _block = new(StringComparer.Ordinal);

    /// <summary>
    /// Feeds one line. Returns a report when the line completes a block; otherwise <see langword="null"/>.
    /// </summary>
    public EncodeProgress? Accept(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var equals = line.IndexOf('=');
        if (equals <= 0)
        {
            return null;
        }

        var key = line[..equals].Trim();
        var value = line[(equals + 1)..].Trim();

        if (key != "progress")
        {
            _block[key] = value;
            return null;
        }

        var progress = Build(value == "end");
        _block.Clear();
        return progress;
    }

    private EncodeProgress Build(bool isFinal) => new()
    {
        Frame = Long("frame"),
        FramesPerSecond = Double("fps"),
        BitRateKbps = Double("bitrate", "kbits/s"),
        TotalSizeBytes = Long("total_size"),
        ProcessedTime = Microseconds("out_time_us") ?? Microseconds("out_time_ms") ?? Clock("out_time"),
        Speed = Double("speed", "x"),
        DuplicatedFrames = Long("dup_frames"),
        DroppedFrames = Long("drop_frames"),
        IsFinal = isFinal,
    };

    private string? Raw(string key) =>
        _block.TryGetValue(key, out var value) && value.Length > 0 && value != "N/A" ? value : null;

    private long? Long(string key) =>
        long.TryParse(Raw(key), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : null;

    private double? Double(string key, string? unit = null)
    {
        var text = Raw(key);
        if (text is not null && unit is not null && text.EndsWith(unit, StringComparison.Ordinal))
        {
            text = text[..^unit.Length];
        }

        return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && double.IsFinite(value) && value >= 0
            ? value
            : null;
    }

    private TimeSpan? Microseconds(string key) =>
        Long(key) is { } micros ? TimeSpan.FromMicroseconds(micros) : null;

    private TimeSpan? Clock(string key)
    {
        var text = Raw(key);
        if (text is null || text.StartsWith('-'))
        {
            return null;
        }

        var parts = text.Split(':');
        return parts.Length == 3
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
               && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
               && double.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds)
            : null;
    }
}
