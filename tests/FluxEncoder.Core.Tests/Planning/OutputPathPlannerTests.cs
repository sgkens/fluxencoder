using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Planning;

public sealed class OutputPathPlannerTests
{
    private static EncodingJob WithPolicy(string output, OverwritePolicy policy) =>
        Job(output) with { Policy = JobPolicy.Default with { Overwrite = policy } };

    private static Result<PlannedJob> Plan(EncodingJob job, FakeFileSystem fs, IReadOnlySet<string>? reserved = null) =>
        EncodePlanner.Plan(job, Input(), Context(fs) with { ReservedOutputs = reserved ?? new HashSet<string>() });

    [Theory]
    [InlineData(OverwritePolicy.Never)]
    [InlineData(OverwritePolicy.Overwrite)]
    [InlineData(OverwritePolicy.Rename)]
    [InlineData(OverwritePolicy.Skip)]
    public void OutputIsInput_IsRejectedUnderEveryPolicy(OverwritePolicy policy)
    {
        var fs = new FakeFileSystem { Files = { "/media/movie.mkv" } };

        var result = Plan(WithPolicy("/media/movie.mkv", policy), fs);

        Assert.Equal(FailureCategory.OutputIsInput, result.Failure?.Category);
    }

    [Fact]
    public void RelativeOutputResolvingToInput_IsRejected()
    {
        var result = Plan(Job("movie.mkv"), new FakeFileSystem());

        Assert.Equal(FailureCategory.OutputIsInput, result.Failure?.Category);
    }

    [Fact]
    public void Existing_Never_Fails()
    {
        var fs = new FakeFileSystem { Files = { "/media/out/a.mkv" } };

        var failure = Plan(Job("/media/out/a.mkv"), fs).Failure;

        Assert.Equal(FailureCategory.OutputExists, failure?.Category);
        Assert.Contains("--overwrite", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_Skip_SkipsWithoutPlan()
    {
        var fs = new FakeFileSystem { Files = { "/media/out/a.mkv" } };

        var planned = Plan(WithPolicy("/media/out/a.mkv", OverwritePolicy.Skip), fs).Value!;

        Assert.True(planned.IsSkipped);
        Assert.Null(planned.Plan);
        Assert.Equal("/media/out/a.mkv", planned.OutputPath);
    }

    [Fact]
    public void Existing_Rename_UsesFirstFreeNumber()
    {
        var fs = new FakeFileSystem { Files = { "/media/out/a.mkv", "/media/out/a (1).mkv" } };

        var planned = Plan(WithPolicy("/media/out/a.mkv", OverwritePolicy.Rename), fs).Value!;

        Assert.Equal("/media/out/a (2).mkv", planned.Plan!.FinalOutputPath);
        Assert.Equal("/media/out/.a (2).tok1.partial.mkv", planned.Plan.TemporaryOutputPath);
        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.OutputRenamed);
    }

    [Fact]
    public void Existing_Overwrite_KeepsName()
    {
        var fs = new FakeFileSystem { Files = { "/media/out/a.mkv" } };

        var planned = Plan(WithPolicy("/media/out/a.mkv", OverwritePolicy.Overwrite), fs).Value!;

        Assert.Equal("/media/out/a.mkv", planned.Plan!.FinalOutputPath);
    }

    [Theory]
    [InlineData(OverwritePolicy.Never)]
    [InlineData(OverwritePolicy.Overwrite)]
    [InlineData(OverwritePolicy.Skip)]
    public void BatchCollision_FailsUnlessRenaming(OverwritePolicy policy)
    {
        var reserved = new HashSet<string> { "/media/out/a.mkv" };

        var result = Plan(WithPolicy("/media/out/a.mkv", policy), new FakeFileSystem(), reserved);

        Assert.Equal(FailureCategory.OutputExists, result.Failure?.Category);
        Assert.Contains("batch", result.Failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchCollision_RenameAvoidsReservedName()
    {
        var reserved = new HashSet<string> { "/media/out/a.mkv", "/media/out/a (1).mkv" };

        var planned = Plan(WithPolicy("/media/out/a.mkv", OverwritePolicy.Rename), new FakeFileSystem(), reserved).Value!;

        Assert.Equal("/media/out/a (2).mkv", planned.Plan!.FinalOutputPath);
    }

    [Fact]
    public void MissingDirectory_IsAllowedAndCreatedAtRunTime()
    {
        var planned = Plan(Job("/media/out/new/deeper/a.mkv"), new FakeFileSystem()).Value!;

        Assert.Equal("/media/out/new/deeper/.a.tok1.partial.mkv", planned.Plan!.TemporaryOutputPath);
    }

    [Fact]
    public void PathThroughAFile_IsNotWritable()
    {
        var fs = new FakeFileSystem { Files = { "/media/out/notes.txt" } };

        var result = Plan(Job("/media/out/notes.txt/a.mkv"), fs);

        Assert.Equal(FailureCategory.OutputNotWritable, result.Failure?.Category);
    }

    [Fact]
    public void ReadOnlyDirectory_IsNotWritable()
    {
        var fs = new FakeFileSystem { ReadOnly = { "/media/out" } };

        Assert.Equal(FailureCategory.OutputNotWritable, Plan(Job("/media/out/a.mkv"), fs).Failure?.Category);
    }

    [Fact]
    public void AlmostNoSpace_Fails()
    {
        var fs = new FakeFileSystem { FreeSpace = 10L * 1024 * 1024 };

        Assert.Equal(FailureCategory.InsufficientSpace, Plan(Job("/media/out/a.mkv"), fs).Failure?.Category);
    }

    [Fact]
    public void LowSpace_Warns()
    {
        var fs = new FakeFileSystem { FreeSpace = 2L * 1024 * 1024 * 1024 };

        var planned = Plan(Job("/media/out/a.mkv", new VideoTarget.StreamCopy()), fs).Value!;

        Assert.Contains(planned.Warnings, w => w.Code == WarningCodes.LowDiskSpace);
    }

    [Fact]
    public void UnknownFreeSpace_DoesNotWarn()
    {
        var fs = new FakeFileSystem { FreeSpace = null };

        var planned = Plan(Job("/media/out/a.mkv", new VideoTarget.StreamCopy()), fs).Value!;

        Assert.DoesNotContain(planned.Warnings, w => w.Code == WarningCodes.LowDiskSpace);
    }

    [Theory]
    [InlineData("rtmp://live/app/key.flv")]
    [InlineData("-")]
    public void NonFileOutput_IsNotSupportedYet(string output)
    {
        Assert.Equal(FailureCategory.InvalidUsage, Plan(Job(output), new FakeFileSystem()).Failure?.Category);
    }
}
