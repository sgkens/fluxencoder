namespace FluxEncoder.Core.Media;

public sealed record MediaChapter
{
    public MediaChapter(TimeSpan start, TimeSpan end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        Start = start;
        End = end;
    }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }

    public string? Title { get; init; }

    public IReadOnlyDictionary<string, string> Tags { get; init; } = MediaTags.Empty;

    public TimeSpan Duration => End - Start;
}
