using FluxEncoder.Core.Media;

namespace FluxEncoder.Core.Tests.Media;

public sealed class InputSourceTests
{
    [Theory]
    [InlineData("movie.mkv", InputKind.LocalFile, null)]
    [InlineData("/media/My Movie.mkv", InputKind.LocalFile, null)]
    [InlineData(@"C:\Videos\movie.mkv", InputKind.LocalFile, null)]
    [InlineData("C:/Videos/movie.mkv", InputKind.LocalFile, null)]
    [InlineData(@"\\server\share\movie.mkv", InputKind.LocalFile, null)]
    [InlineData("file:movie.mkv", InputKind.LocalFile, "file")]
    [InlineData("file:///tmp/movie.mkv", InputKind.LocalFile, "file")]
    [InlineData("https://example.com/a.m3u8", InputKind.Network, "https")]
    [InlineData("RTMP://live.example/app/key", InputKind.Network, "rtmp")]
    [InlineData("srt://10.0.0.1:9000", InputKind.Network, "srt")]
    [InlineData("udp://239.0.0.1:1234", InputKind.Network, "udp")]
    [InlineData("-", InputKind.Pipe, null)]
    [InlineData("pipe:0", InputKind.Pipe, null)]
    [InlineData("fd:3", InputKind.Pipe, null)]
    [InlineData("/dev/stdin", InputKind.Pipe, null)]
    [InlineData("/dev/video0", InputKind.Device, null)]
    [InlineData("weird name://not-a-scheme.mkv", InputKind.LocalFile, null)]
    public void Classify(string input, InputKind kind, string? scheme)
    {
        var source = InputSource.Classify(input);

        Assert.Equal(input, source.Value);
        Assert.Equal(kind, source.Kind);
        Assert.Equal(scheme, source.Scheme);
    }

    [Fact]
    public void LocalFiles_AreSeekableAndProbeable()
    {
        var source = InputSource.Classify("movie.mkv");

        Assert.True(source.IsSeekable);
        Assert.True(source.CanProbe);
        Assert.Equal("movie.mkv", source.LocalPath);
    }

    [Fact]
    public void Pipes_AreNeitherSeekableNorProbeable()
    {
        var source = InputSource.Classify("pipe:0");

        Assert.False(source.IsSeekable);
        Assert.False(source.CanProbe);
        Assert.Null(source.LocalPath);
    }

    [Fact]
    public void Network_IsProbeableButNotSeekable()
    {
        var source = InputSource.Classify("https://example.com/v.mp4");

        Assert.False(source.IsSeekable);
        Assert.True(source.CanProbe);
        Assert.Null(source.LocalPath);
    }

    [Theory]
    [InlineData("file:movie.mkv", "movie.mkv")]
    [InlineData("file:///tmp/My%20Movie.mkv", "/tmp/My Movie.mkv")]
    public void FileScheme_ResolvesToLocalPath(string input, string expected)
    {
        Assert.Equal(expected, InputSource.Classify(input).LocalPath);
    }
}
