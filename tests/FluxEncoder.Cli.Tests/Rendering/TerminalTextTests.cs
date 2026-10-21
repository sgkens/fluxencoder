using FluxEncoder.Cli.Rendering;

namespace FluxEncoder.Cli.Tests.Rendering;

public sealed class TerminalTextTests
{
    [Theory]
    [InlineData("movie.mkv", "movie.mkv")]
    [InlineData("Ünïcødé 映画.mkv", "Ünïcødé 映画.mkv")]
    [InlineData("line one\nline two", "line one\nline two")]
    [InlineData("a\tb", "a b")]
    [InlineData("evil\u001b]0;pwned\u0007.mkv", "evil?]0;pwned?.mkv")]
    [InlineData("red\u001b[31mtext", "red?[31mtext")]
    [InlineData("c1\u009b31m", "c1?31m")]
    [InlineData("carriage\roverwrite", "carriage?overwrite")]
    [InlineData("invoice‮gnp.exe", "invoice?gnp.exe")]
    [InlineData("isolate⁦x⁩", "isolate?x?")]
    [InlineData("del\u007F", "del?")]
    public void Clean_NeutralizesControlAndBidiCharacters(string input, string expected)
    {
        Assert.Equal(expected, TerminalText.Clean(input));
    }

    [Fact]
    public void Clean_HandlesNull()
    {
        Assert.Equal(string.Empty, TerminalText.Clean(null));
    }
}
