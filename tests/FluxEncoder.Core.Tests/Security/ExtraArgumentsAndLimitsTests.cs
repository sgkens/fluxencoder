using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Security;

public sealed class ExtraArgumentsAndLimitsTests
{
    private static IReadOnlyList<string> Arguments(PlannedJob planned) => planned.Plan!.Invocations[^1].Arguments;

    [Fact]
    public void ExtraArguments_GoAmongOutputOptionsWithRedactedWarning()
    {
        var job = Job("/media/out/a.mkv") with { ExtraArguments = ["-tune", "film", "-headers", "Authorization: Bearer x"] };

        var planned = EncodePlanner.Plan(job, Input(), Context()).Value!;
        var args = Arguments(planned);

        var start = args.ToList().IndexOf("-tune");
        Assert.Equal(["-tune", "film", "-headers", "Authorization: Bearer x", "-n", "-f", "matroska"], args.Skip(start).Take(7));
        var warning = Assert.Single(planned.Warnings, w => w.Code == WarningCodes.ExtraArguments);
        Assert.Contains("-tune film -headers ***", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtraArguments_AreRefusedWhenPolicyDisallows()
    {
        var job = Job("/media/out/a.mkv") with { ExtraArguments = ["-tune", "film"] };
        var context = Context() with { Policy = SourcePolicy.Default with { AllowExtraArguments = false } };

        Assert.Equal(FailureCategory.PolicyDenied, EncodePlanner.Plan(job, Input(), context).Failure?.Category);
    }

    [Fact]
    public void ExtraArguments_RejectNull()
    {
        Assert.Throws<ArgumentException>(() => Job("/media/out/a.mkv") with { ExtraArguments = ["-x", null!] });
    }

    [Fact]
    public void Limits_AddOutputSizeAndThreadOptions()
    {
        var context = Context() with { Limits = new ResourceLimits { MaxOutputBytes = 50L * 1024 * 1024, Threads = 4 } };

        var planned = EncodePlanner.Plan(Job("/media/out/a.mkv"), Input(), context).Value!;
        var joined = string.Join(' ', Arguments(planned));

        Assert.Contains("-fs 52428800 -threads 4 -filter_threads 4 -n -f matroska", joined, StringComparison.Ordinal);
        Assert.Equal(50L * 1024 * 1024, planned.Plan!.MaxOutputBytes);
    }

    [Fact]
    public void Limits_RefuseOversizedInput()
    {
        var context = Context() with { Limits = new ResourceLimits { MaxInputBytes = 1024L * 1024 * 1024 } };

        var failure = EncodePlanner.Plan(Job("/media/out/a.mkv"), Input(), context).Failure;

        Assert.Equal(FailureCategory.PolicyDenied, failure?.Category);
        Assert.Contains("1024 MB limit", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Limits_ValidateRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimits { Threads = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimits { MaxOutputBytes = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimits { MaxRunTime = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimits { MaxInputBytes = 0 });
    }
}
