using FluxEncoder.Core.Tooling;

namespace FluxEncoder.Core.Tests.Tooling;

public sealed class FormatCatalogTests
{
    private static readonly FormatCatalog Demuxers = new(
    [
        new FormatEntry(["matroska", "webm"], "Matroska / WebM"),
        new FormatEntry(["mov", "mp4", "m4a", "3gp", "3g2", "mj2"], "QuickTime / MOV"),
        new FormatEntry(["mpegts"], "MPEG-TS"),
        new FormatEntry(["alsa"], "ALSA audio input", isDevice: true),
        new FormatEntry(["webm"], "Shadowing alias listed later"),
    ]);

    [Theory]
    [InlineData("matroska", "matroska")]
    [InlineData("webm", "matroska")]
    [InlineData("mp4", "mov")]
    [InlineData("m4a", "mov")]
    [InlineData("matroska,webm", "matroska")]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "mov")]
    [InlineData("unknown,mp4", "mov")]
    [InlineData(" mpegts ", "mpegts")]
    public void TryFind_ResolvesAnyAlias(string name, string expected)
    {
        Assert.True(Demuxers.TryFind(name, out var entry));
        Assert.Equal(expected, entry.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("avi")]
    [InlineData("Matroska")]
    [InlineData("mp")]
    public void TryFind_RejectsUnknownNames(string? name)
    {
        Assert.False(Demuxers.TryFind(name, out _));
        Assert.False(Demuxers.Contains(name));
    }

    [Fact]
    public void FirstListedEntryWinsForDuplicateAlias()
    {
        Assert.True(Demuxers.TryFind("webm", out var entry));
        Assert.Equal("Matroska / WebM", entry.Description);
    }

    [Theory]
    [InlineData("webm", "matroska,webm", true)]
    [InlineData("mp4", "mov", true)]
    [InlineData("mp4", "matroska", false)]
    [InlineData("avi", "avi", false)]
    public void AreSameFormat_ComparesResolvedEntries(string first, string second, bool expected)
    {
        Assert.Equal(expected, Demuxers.AreSameFormat(first, second));
    }

    [Fact]
    public void Enumerates_InListingOrder()
    {
        Assert.Equal(5, Demuxers.Count);
        Assert.Equal("mov", Demuxers[1].Name);
        Assert.True(Demuxers[3].IsDevice);
        Assert.Equal(["matroska", "mov", "mpegts", "alsa", "webm"], Demuxers.Select(e => e.Name));
    }

    [Fact]
    public void FormatEntry_RequiresAName()
    {
        Assert.Throws<ArgumentException>(() => new FormatEntry([" ", ""], "x"));
        Assert.Equal(["a", "b"], new FormatEntry([" a", "b "], "x").Names);
    }
}
