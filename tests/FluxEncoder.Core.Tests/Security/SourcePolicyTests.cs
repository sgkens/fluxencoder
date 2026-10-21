using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Security;

public sealed class SourcePolicyTests
{
    private static string Full(string path) => path.StartsWith('/') ? path : "/work/" + path;

    [Theory]
    [InlineData("movie.mkv", "file,crypto,data")]
    [InlineData("pipe:0", "pipe")]
    [InlineData("https://cdn.example/a.m3u8", "http,https,tcp,tls,crypto,httpproxy")]
    [InlineData("srt://host:9000", "srt,udp")]
    [InlineData("rtsp://cam/stream", "rtsp,rtsps,rtp,srtp,udp,tcp,tls,http,https")]
    public void Whitelist_MatchesTheInputsOwnScheme(string input, string expected)
    {
        Assert.Equal(expected, SourcePolicy.ProtocolWhitelist(InputSource.Classify(input)));
    }

    [Fact]
    public void Devices_HaveNoWhitelist()
    {
        Assert.Null(SourcePolicy.ProtocolWhitelist(InputSource.Classify("/dev/video0")));
    }

    [Theory]
    [InlineData("http:evil.mkv", "/work/http:evil.mkv")]
    [InlineData("concat:a.mkv|b.mkv", "/work/concat:a.mkv|b.mkv")]
    [InlineData("-dash.mkv", "/work/-dash.mkv")]
    [InlineData("file:movie.mkv", "/work/movie.mkv")]
    public void ProtocolLikeLocalNames_ReachFFmpegAsAbsolutePaths(string input, string expected)
    {
        Assert.Equal(["-protocol_whitelist", SourcePolicy.LocalWhitelist, "-i", expected], SourcePolicy.FFmpegInput(InputSource.Classify(input), Full));
    }

    [Fact]
    public void NetworkInput_IsPassedUnchanged()
    {
        var arguments = SourcePolicy.FFmpegInput(InputSource.Classify("https://u:p@cdn/x.mp4"), Full);

        Assert.Equal("https://u:p@cdn/x.mp4", arguments[^1]);
    }

    [Theory]
    [InlineData("gopher://old/x")]
    [InlineData("ws://host/socket")]
    public void UnknownSchemes_AreDenied(string input)
    {
        var source = InputSource.Classify(input);

        Assert.Equal(InputKind.Network, source.Kind);
        Assert.Equal(FailureCategory.PolicyDenied, SourcePolicy.Default.CheckInput(source, Full)?.Category);
    }

    [Fact]
    public void RestrictedPolicy_DeniesKindsAndSchemes()
    {
        var policy = SourcePolicy.Default with { AllowPipes = false, AllowDevices = false, AllowedSchemes = ["https"] };

        Assert.NotNull(policy.CheckInput(InputSource.Classify("pipe:0"), Full));
        Assert.NotNull(policy.CheckInput(InputSource.Classify("/dev/video0"), Full));
        Assert.NotNull(policy.CheckInput(InputSource.Classify("http://x/y"), Full));
        Assert.Null(policy.CheckInput(InputSource.Classify("https://x/y"), Full));
        Assert.Equal(ExitCode.InvalidUsage, policy.CheckInput(InputSource.Classify("pipe:0"), Full)!.ExitCode);
    }

    [Fact]
    public void AllowedRoots_ConfineLocalInputsAndOutputs()
    {
        var policy = SourcePolicy.Default with { AllowedRoots = ["/srv/media"] };

        Assert.Null(policy.CheckInput(InputSource.Classify("/srv/media/in/a.mkv"), Full));
        Assert.NotNull(policy.CheckInput(InputSource.Classify("/srv/mediaevil/a.mkv"), Full));
        Assert.NotNull(policy.CheckInput(InputSource.Classify("/etc/passwd"), Full));
        Assert.Null(policy.CheckOutput("/srv/media/out/a.mkv"));
        Assert.NotNull(policy.CheckOutput("/tmp/a.mkv"));
    }

    [Fact]
    public void Planner_RefusesDeniedInputAndOutputBeforePlanning()
    {
        var restricted = Context() with { Policy = SourcePolicy.Default with { AllowedRoots = ["/media/out"] } };

        var input = EncodePlanner.Plan(Job("/media/out/a.mkv"), Input(), restricted).Failure;
        Assert.Equal(FailureCategory.PolicyDenied, input?.Category);

        var media = Movie() with { Source = "/media/out/movie.mkv" };
        var output = EncodePlanner.Plan(Job("/elsewhere/a.mkv"), new PlanningInput(InputSource.Classify(media.Source), media), restricted).Failure;
        Assert.Equal(FailureCategory.PolicyDenied, output?.Category);
    }
}
