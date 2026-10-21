using System.Globalization;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;

namespace FluxEncoder.Cli.Parsing;

/// <summary>
/// Parses the free-form option values the CLI accepts. Each parser returns an error message instead of throwing, so
/// System.CommandLine reports every problem as a usage error.
/// </summary>
internal static class ValueParsers
{
    /// <summary>
    /// Accepts seconds (<c>90</c>, <c>90.5</c>), clock form (<c>1:30</c>, <c>1:02:03.25</c>), and unit form
    /// (<c>90s</c>, <c>2.5m</c>, <c>1h30m</c>).
    /// </summary>
    public static bool TryParseTime(string? text, out TimeSpan time, out string? error)
    {
        time = default;
        error = null;
        text = text?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            error = "A time is required.";
            return false;
        }

        if (text.Contains(':', StringComparison.Ordinal))
        {
            var parts = text.Split(':');
            if (parts.Length is 2 or 3
                && parts[..^1].All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                && double.TryParse(parts[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var clockSeconds)
                && clockSeconds < 60)
            {
                var numbers = parts[..^1].Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
                if (numbers.Length == 2 && numbers[1] >= 60)
                {
                    error = $"'{text}' has more than 59 minutes in the minutes field.";
                    return false;
                }

                var minutes = numbers.Length == 2 ? numbers[0] * 60 + numbers[1] : numbers[0];
                time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(clockSeconds);
                return true;
            }

            error = $"'{text}' is not a valid time. Use seconds (90), m:ss (1:30), or h:mm:ss (1:02:03).";
            return false;
        }

        if (double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            time = TimeSpan.FromSeconds(seconds);
            return true;
        }

        var total = TimeSpan.Zero;
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            var length = 0;
            while (length < rest.Length && (char.IsAsciiDigit(rest[length]) || rest[length] == '.'))
            {
                length++;
            }

            if (length == 0 || length == rest.Length
                || !double.TryParse(rest[..length], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                error = $"'{text}' is not a valid time. Use seconds (90), m:ss (1:30), or units (1h30m, 90s).";
                return false;
            }

            total += char.ToLowerInvariant(rest[length]) switch
            {
                'h' => TimeSpan.FromHours(value),
                'm' => TimeSpan.FromMinutes(value),
                's' => TimeSpan.FromSeconds(value),
                _ => TimeSpan.MinValue,
            };

            if (total < TimeSpan.Zero)
            {
                error = $"'{text}' has an unknown unit; use h, m, or s.";
                return false;
            }

            rest = rest[(length + 1)..];
        }

        time = total;
        return true;
    }

    /// <summary>
    /// Accepts kbit/s (<c>4000</c>, <c>4000k</c>) or Mbit/s (<c>4M</c>, <c>4.5M</c>).
    /// </summary>
    public static bool TryParseBitRate(string? text, out int kbps, out string? error)
    {
        kbps = 0;
        error = null;
        text = text?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            error = "A bit rate is required.";
            return false;
        }

        var multiplier = 1.0;
        var number = text;
        if (text.EndsWith('k') || text.EndsWith('K'))
        {
            number = text[..^1];
        }
        else if (text.EndsWith('m') || text.EndsWith('M'))
        {
            number = text[..^1];
            multiplier = 1000;
        }

        if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            error = $"'{text}' is not a valid bit rate. Use kbit/s (4000 or 4000k) or Mbit/s (4M).";
            return false;
        }

        var result = value * multiplier;
        if (result < 1 || result > 1_000_000)
        {
            error = $"'{text}' is outside the supported range of 1 kbit/s to 1000 Mbit/s.";
            return false;
        }

        kbps = (int)Math.Round(result);
        return true;
    }

    /// <summary>
    /// Accepts <c>all</c>, <c>default</c>, <c>none</c>, or a comma-separated language preference list (<c>jpn,eng</c>).
    /// </summary>
    public static bool TryParseTracks(string? text, out TrackSelection selection, out string? error)
    {
        selection = new TrackSelection.All();
        error = null;
        text = text?.Trim();

        switch (text?.ToLowerInvariant())
        {
            case "all":
                return true;
            case "default":
                selection = new TrackSelection.Default();
                return true;
            case "none":
                selection = new TrackSelection.None();
                return true;
        }

        var languages = new List<LanguageTag>();
        foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!LanguageTag.TryParse(part, out var tag))
            {
                error = $"'{part}' is not a language code. Use all, default, none, or codes such as eng,jpn.";
                return false;
            }

            languages.Add(tag);
        }

        if (languages.Count == 0)
        {
            error = "Use all, default, none, or language codes such as eng,jpn.";
            return false;
        }

        selection = new TrackSelection.Languages(languages);
        return true;
    }
}
