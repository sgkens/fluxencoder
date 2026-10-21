using FluxEncoder.Core.Security;

namespace FluxEncoder.Core.Tests.Security;

public sealed class RemoteMountTests
{
    private static RemoteMount Mount(string url, string alias = "nas")
    {
        var (mount, error) = RemoteMount.Parse(alias, url);
        Assert.True(mount is not null, error);
        return mount;
    }

    [Fact]
    public void Parse_ReadsAllParts()
    {
        var mount = Mount("sftp://jane.doe@nas.local:2222/media/films");

        Assert.Equal(("sftp", "nas.local", 2222, "jane.doe", "media/films"), (mount.Protocol, mount.Host, mount.Port!.Value, mount.User!, mount.BasePath!));
        Assert.Equal("sftp://jane.doe@nas.local:2222/media/films", mount.ToDisplayUrl());
    }

    [Fact]
    public void Parse_DefaultPortAndNoUser()
    {
        var mount = Mount("ftp://files.example/");

        Assert.Null(mount.Port);
        Assert.Null(mount.User);
        Assert.Null(mount.BasePath);
    }

    [Theory]
    [InlineData("nas", "sftp://jane:secret@nas/media", "Do not put the password in the URL")]
    [InlineData("bad alias", "sftp://nas/media", "not a valid alias")]
    [InlineData("nas", "gopher://nas/x", "must be one of")]
    [InlineData("nas", "not a url", "is not a URL")]
    public void Parse_RefusesUnsafeOrInvalid(string alias, string url, string expected)
    {
        var (mount, error) = RemoteMount.Parse(alias, url);

        Assert.Null(mount);
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToUrl_PercentEncodesUserPasswordAndEverySegment()
    {
        var mount = new RemoteMount("nas", "ftp", "files.example", 2121, "a@b:c", "base dir");

        var url = mount.ToUrl("My Movies/a#1?x.mkv", "p@ss:w/rd#?%");

        Assert.Equal("ftp://a%40b%3Ac:p%40ss%3Aw%2Frd%23%3F%25@files.example:2121/base%20dir/My%20Movies/a%231%3Fx.mkv", url);
        var parsed = new Uri(url);
        Assert.Equal("files.example", parsed.Host);
        Assert.Equal(2121, parsed.Port);
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("films/../../x")]
    [InlineData("./x")]
    [InlineData("")]
    [InlineData("/")]
    public void ToUrl_RefusesEscapingOrEmptyPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => Mount("sftp://nas/media").ToUrl(path, null));
    }

    [Fact]
    public void ToUrl_BracketsIpv6Hosts()
    {
        Assert.Equal("sftp://[::1]/a.mkv", new RemoteMount("v6", "sftp", "::1").ToUrl("a.mkv", null));
    }

    [Theory]
    [InlineData("remote://nas/films/a.mkv", "nas", "films/a.mkv")]
    [InlineData("REMOTE://nas/a.mkv", "nas", "a.mkv")]
    [InlineData("remote://nas", "nas", "")]
    public void SplitRemoteInput(string input, string alias, string path)
    {
        Assert.Equal((alias, path), RemoteMount.SplitRemoteInput(input));
    }

    [Fact]
    public void SplitRemoteInput_IgnoresOtherInputs()
    {
        Assert.Null(RemoteMount.SplitRemoteInput("sftp://nas/a.mkv"));
    }
}
