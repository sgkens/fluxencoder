using System.Globalization;

namespace FluxEncoder.Core.Tooling;

/// <summary>
/// The FFmpeg version string as reported, with the release number when one can be read.
/// </summary>
/// <remarks>
/// Release builds report <c>8.0.1</c> or <c>n7.1</c>; distribution builds add suffixes
/// (<c>6.1.1-3ubuntu5</c>); git snapshots (<c>N-118000-gabc1234</c>) and some Windows builds
/// (<c>2024-12-01-git-…</c>) have no release number, so <see cref="Major"/> is then <see langword="null"/>.
/// Gate features on capabilities (encoders, filters), not on the version.
/// </remarks>
public sealed record FFmpegVersion
{
    public FFmpegVersion(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        Raw = raw;

        var span = raw.AsSpan();
        if (span.StartsWith("n"))
        {
            span = span[1..];
        }

        var numbers = new List<int>(3);
        while (numbers.Count < 3)
        {
            var length = 0;
            while (length < span.Length && char.IsAsciiDigit(span[length]))
            {
                length++;
            }

            if (length == 0 || !int.TryParse(span[..length], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                break;
            }

            numbers.Add(number);
            span = span[length..];

            if (span.IsEmpty || span[0] != '.')
            {
                break;
            }

            span = span[1..];
        }

        // A lone leading number followed by '-' is a date (2024-12-01) or nothing useful, not a release.
        if (numbers.Count >= 2)
        {
            Major = numbers[0];
            Minor = numbers[1];
            Patch = numbers.Count > 2 ? numbers[2] : null;
        }
    }

    public string Raw { get; }

    public int? Major { get; }

    public int? Minor { get; }

    public int? Patch { get; }

    public override string ToString() => Raw;
}
