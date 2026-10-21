using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Tests.Results;

public sealed class BatchResultTests
{
    private static JobResult Ok() => JobResult.Succeeded("ok.mkv");

    private static JobResult Skip() => JobResult.Skipped("skip.mkv", "Output exists.");

    private static JobResult Fail(FailureCategory category) =>
        JobResult.Failed("fail.mkv", new JobFailure(category, category.ToString()));

    [Fact]
    public void Empty_Succeeds()
    {
        Assert.Equal(ExitCode.Success, new BatchResult([]).ExitCode);
    }

    [Fact]
    public void SuccessAndSkips_Succeed()
    {
        var batch = new BatchResult([Ok(), Skip()]);

        Assert.Equal(ExitCode.Success, batch.ExitCode);
        Assert.Equal(1, batch.SucceededCount);
        Assert.Equal(1, batch.SkippedCount);
    }

    [Fact]
    public void SingleFailure_ReturnsItsOwnCode()
    {
        Assert.Equal(ExitCode.InputError, new BatchResult([Fail(FailureCategory.InputNotFound)]).ExitCode);
    }

    [Fact]
    public void MixedSuccessAndFailure_IsPartialFailure()
    {
        Assert.Equal(ExitCode.PartialFailure, new BatchResult([Ok(), Fail(FailureCategory.EncodeFailed)]).ExitCode);
        Assert.Equal(ExitCode.PartialFailure, new BatchResult([Skip(), Fail(FailureCategory.EncodeFailed)]).ExitCode);
    }

    [Fact]
    public void AllFailedSameCode_ReturnsThatCode()
    {
        var batch = new BatchResult([Fail(FailureCategory.OutputExists), Fail(FailureCategory.OutputNotWritable)]);

        Assert.Equal(ExitCode.OutputError, batch.ExitCode);
    }

    [Fact]
    public void AllFailedDifferentCodes_IsGeneralFailure()
    {
        var batch = new BatchResult([Fail(FailureCategory.OutputExists), Fail(FailureCategory.EncodeFailed)]);

        Assert.Equal(ExitCode.GeneralFailure, batch.ExitCode);
    }

    [Fact]
    public void Cancellation_TakesPriority()
    {
        var batch = new BatchResult([Ok(), Fail(FailureCategory.EncodeFailed), Fail(FailureCategory.Cancelled)]) { NotStarted = 4 };

        Assert.Equal(ExitCode.Cancelled, batch.ExitCode);
        Assert.Equal(4, batch.NotStarted);
    }
}
