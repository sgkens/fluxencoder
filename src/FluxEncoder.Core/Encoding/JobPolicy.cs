namespace FluxEncoder.Core.Encoding;

/// <summary>
/// How a job treats existing files, failed output, and transient errors. <see cref="Default"/> is the safe
/// behaviour with no flags: never overwrite, delete partial output, no retry.
/// </summary>
public sealed record JobPolicy
{
    public static JobPolicy Default { get; } = new();

    public OverwritePolicy Overwrite
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null);
    } = OverwritePolicy.Never;

    public PartialOutputPolicy PartialOutput
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null);
    } = PartialOutputPolicy.Delete;

    public RetryPolicy Retry
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = RetryPolicy.None;
}
