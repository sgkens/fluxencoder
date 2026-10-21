using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Tests.Results;

public sealed class JobResultTests
{
    private static readonly JobFailure EncodeFailure = new(FailureCategory.EncodeFailed, "FFmpeg exited with code 1.");

    [Fact]
    public void Succeeded_HasNoFailureAndExitsZero()
    {
        var result = JobResult.Succeeded("out.mkv", [new JobWarning("subtitle-dropped", "Stream 3 was dropped.")]);

        Assert.Equal(JobOutcome.Succeeded, result.Outcome);
        Assert.Null(result.Failure);
        Assert.Equal(ExitCode.Success, result.ExitCode);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Skipped_RequiresReasonAndExitsZero()
    {
        var result = JobResult.Skipped("out.mkv", "Output exists.");

        Assert.Equal(JobOutcome.Skipped, result.Outcome);
        Assert.Equal(ExitCode.Success, result.ExitCode);
        Assert.Throws<ArgumentException>(() => JobResult.Skipped("out.mkv", " "));
    }

    [Fact]
    public void Failed_UsesFailureExitCode()
    {
        var result = JobResult.Failed("out.mkv", EncodeFailure);

        Assert.Equal(JobOutcome.Failed, result.Outcome);
        Assert.Equal(ExitCode.EncodeFailed, result.ExitCode);
    }

    [Fact]
    public void Attempts_DefaultsToOneAndCannotBeZero()
    {
        Assert.Equal(1, JobResult.Succeeded("out.mkv").Attempts);
        Assert.Throws<ArgumentOutOfRangeException>(() => JobResult.Succeeded("out.mkv") with { Attempts = 0 });
    }

    [Fact]
    public void Equality_ComparesWarningsBySequence()
    {
        var a = JobResult.Succeeded("out.mkv", [new JobWarning("w", "m")]);
        var b = JobResult.Succeeded("out.mkv", [new JobWarning("w", "m")]);

        Assert.Equal(a, b);
        Assert.NotEqual(a, JobResult.Succeeded("out.mkv"));
    }

    [Theory]
    [InlineData("subtitle-dropped")]
    [InlineData("hdr10plus-lost")]
    [InlineData("w")]
    public void WarningCode_AcceptsKebabCase(string code)
    {
        Assert.Equal(code, new JobWarning(code, "message").Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Subtitle-Dropped")]
    [InlineData("subtitle_dropped")]
    [InlineData("-dropped")]
    [InlineData("dropped-")]
    [InlineData("a--b")]
    [InlineData("1st")]
    public void WarningCode_RejectsOtherForms(string code)
    {
        Assert.Throws<ArgumentException>(() => new JobWarning(code, "message"));
    }

    [Fact]
    public void Failure_RejectsBlankMessageAndUndefinedCategory()
    {
        Assert.Throws<ArgumentException>(() => new JobFailure(FailureCategory.Internal, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JobFailure((FailureCategory)99, "x"));
    }
}
