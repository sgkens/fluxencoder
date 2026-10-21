namespace FluxEncoder.Core.Results;

/// <summary>
/// A classified job failure. Code paths report failures as values of this type instead of throwing a
/// generic <see cref="Exception"/> (TSKE004.6).
/// </summary>
public sealed record JobFailure
{
    public JobFailure(FailureCategory category, string message)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, null);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Category = category;
        Message = message;
    }

    public FailureCategory Category { get; }

    /// <summary>
    /// One-line, user-facing explanation.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Supporting diagnostics, such as the tail of FFmpeg's stderr. Shown with <c>--verbose</c> and in JSON.
    /// </summary>
    public string? Detail { get; init; }

    public ExitCode ExitCode => Category.ToExitCode();

    public bool IsTransient => Category.IsTransient();
}
