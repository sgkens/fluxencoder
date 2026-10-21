using System.Diagnostics.CodeAnalysis;

namespace FluxEncoder.Core.Results;

/// <summary>
/// A value or the classified failure that prevented it. Used where a failure is an expected outcome
/// (a missing tool, an unreadable input) rather than a bug.
/// </summary>
public sealed record Result<T>
{
    private Result(T? value, JobFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public JobFailure? Failure { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool IsSuccess => Failure is null;

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, null);
    }

    public static Result<T> Fail(JobFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(default, failure);
    }

    public static implicit operator Result<T>(JobFailure failure) => Fail(failure);
}
