namespace FluxEncoder.Core.Encoding;

/// <summary>
/// The part of the input to encode.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> and <see cref="End"/> are absolute positions on the input's own timeline, as a media
/// player shows them: <c>--start 1:00 --end 2:00</c> keeps one minute. <see cref="Duration"/> is a length
/// counted from <see cref="Start"/> (or from the beginning): <c>--start 1:00 --duration 30</c> keeps thirty
/// seconds. <see cref="End"/> and <see cref="Duration"/> are alternatives; giving both is an error.
/// </para>
/// <para>
/// For an unbounded input only <see cref="Duration"/> applies, as a recording limit.
/// </para>
/// </remarks>
public sealed record TrimRange
{
    public TrimRange(TimeSpan? start = null, TimeSpan? end = null, TimeSpan? duration = null)
    {
        if (start is null && end is null && duration is null)
        {
            throw new ArgumentException("A trim needs a start, an end, or a duration.");
        }

        if (start is { } s)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(s, TimeSpan.Zero, nameof(start));
        }

        if (end is not null && duration is not null)
        {
            throw new ArgumentException("Give either an end position or a duration, not both.");
        }

        if (end is { } e && e <= (start ?? TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(end), e, "The end must be after the start.");
        }

        if (duration is { } d)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(d, TimeSpan.Zero, nameof(duration));
        }

        Start = start;
        End = end;
        Duration = duration;
    }

    public TimeSpan? Start { get; }

    public TimeSpan? End { get; }

    public TimeSpan? Duration { get; }

    /// <summary>
    /// The length to encode, or <see langword="null"/> when the trim only sets a start (encode to the end).
    /// </summary>
    public TimeSpan? Length => Duration ?? (End is { } end ? end - (Start ?? TimeSpan.Zero) : null);
}
