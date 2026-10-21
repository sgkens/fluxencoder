using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Tests.Encoding;

public sealed class OutputPathsTests
{
    private static readonly string Dir = Path.Combine("media", "out");

    [Fact]
    public void Temporary_StaysInDestinationDirectoryAndKeepsExtensionLast()
    {
        var temp = OutputPaths.Temporary(Path.Combine(Dir, "Movie (2024).mkv"), "a1b2");

        Assert.Equal(Path.Combine(Dir, ".Movie (2024).a1b2.partial.mkv"), temp);
        Assert.Equal(".mkv", Path.GetExtension(temp));
    }

    [Fact]
    public void Temporary_WorksForBareFileName()
    {
        Assert.Equal(".out.x.partial.mp4", OutputPaths.Temporary("out.mp4", "x"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a-b")]
    [InlineData("a/b")]
    [InlineData("é")]
    public void Temporary_RejectsUnsafeTokens(string token)
    {
        Assert.ThrowsAny<ArgumentException>(() => OutputPaths.Temporary("out.mp4", token));
    }

    [Fact]
    public void Temporary_RejectsDirectoryPath()
    {
        Assert.Throws<ArgumentException>(() => OutputPaths.Temporary(Dir + Path.DirectorySeparatorChar, "x"));
    }

    [Fact]
    public void Numbered_InsertsCounterBeforeExtension()
    {
        Assert.Equal(Path.Combine(Dir, "clip (3).webm"), OutputPaths.Numbered(Path.Combine(Dir, "clip.webm"), 3));
    }

    [Fact]
    public void FirstAvailable_ReturnsOriginalWhenFree()
    {
        Assert.Equal("clip.mkv", OutputPaths.FirstAvailable("clip.mkv", _ => false));
    }

    [Fact]
    public void FirstAvailable_SkipsTakenCandidates()
    {
        var taken = new HashSet<string> { "clip.mkv", "clip (1).mkv", "clip (2).mkv" };

        Assert.Equal("clip (3).mkv", OutputPaths.FirstAvailable("clip.mkv", taken.Contains));
    }

    [Fact]
    public void FirstAvailable_GivesUpAfterLimit()
    {
        Assert.Null(OutputPaths.FirstAvailable("clip.mkv", _ => true));
    }
}
