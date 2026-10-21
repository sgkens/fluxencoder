using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Tests.Encoding;

public sealed class OutputPlanTests
{
    private static readonly EncodingJob Job = new()
    {
        InputPath = "in.mkv",
        OutputPath = "out.mkv",
        Video = new VideoTarget.StreamCopy(),
        Audio = new AudioTarget.StreamCopy(),
    };

    [Fact]
    public void Invocation_CopiesArgumentsSoCallerMutationHasNoEffect()
    {
        List<string> arguments = ["-i", "in.mkv", "out.mkv"];
        var invocation = new FFmpegInvocation(arguments);

        arguments[1] = "other.mkv";
        arguments.Add("-y");

        Assert.Equal(["-i", "in.mkv", "out.mkv"], invocation.Arguments);
    }

    [Fact]
    public void Invocation_KeepsArgumentsWithSpacesIntact()
    {
        var invocation = new FFmpegInvocation(["-i", "My Movie (2024).mkv"]);

        Assert.Equal("My Movie (2024).mkv", invocation.Arguments[1]);
    }

    [Fact]
    public void Invocation_EqualityComparesSequences()
    {
        Assert.Equal(new FFmpegInvocation(["-i", "a"]), new FFmpegInvocation(["-i", "a"]));
        Assert.Equal(new FFmpegInvocation(["-i", "a"]).GetHashCode(), new FFmpegInvocation(["-i", "a"]).GetHashCode());
        Assert.NotEqual(new FFmpegInvocation(["-i", "a"]), new FFmpegInvocation(["-i", "A"]));
        Assert.NotEqual(new FFmpegInvocation(["-i", "a"]), new FFmpegInvocation(["-i", "a", "b"]));
    }

    [Fact]
    public void Invocation_RejectsEmptyOrNullArguments()
    {
        Assert.Throws<ArgumentException>(() => new FFmpegInvocation([]));
        Assert.Throws<ArgumentException>(() => new FFmpegInvocation(["-i", null!]));
        Assert.Throws<ArgumentNullException>(() => new FFmpegInvocation(null!));
    }

    [Fact]
    public void Plan_PreservesInvocationOrder()
    {
        var plan = new OutputPlan(Job, [new FFmpegInvocation(["pass1"]), new FFmpegInvocation(["pass2"])]);

        Assert.Equal(["pass1", "pass2"], plan.Invocations.Select(i => i.Arguments[0]));
    }

    [Fact]
    public void Plan_EqualityComparesJobAndInvocations()
    {
        OutputPlan Make(string arg) => new(Job, [new FFmpegInvocation(["-i", arg])]);

        Assert.Equal(Make("a"), Make("a"));
        Assert.Equal(Make("a").GetHashCode(), Make("a").GetHashCode());
        Assert.NotEqual(Make("a"), Make("b"));
        Assert.NotEqual(Make("a"), new OutputPlan(Job with { OutputPath = "x.mkv" }, [new FFmpegInvocation(["-i", "a"])]));
    }

    [Fact]
    public void Plan_RequiresJobAndAtLeastOneInvocation()
    {
        Assert.Throws<ArgumentException>(() => new OutputPlan(Job, []));
        Assert.Throws<ArgumentException>(() => new OutputPlan(Job, [null!]));
        Assert.Throws<ArgumentNullException>(() => new OutputPlan(null!, [new FFmpegInvocation(["-version"])]));
    }
}
