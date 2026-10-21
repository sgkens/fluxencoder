using FluxEncoder.Cli.Batching;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Cli.Tests.Batching;

public sealed class BatchingTests : IDisposable
{
    private static readonly VideoTarget Hevc = new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.Balanced);
    private static readonly VideoTarget Copy = new VideoTarget.StreamCopy();

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-batch-" + Guid.NewGuid().ToString("N"));

    public BatchingTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(params string[] parts) => Path.Combine([_directory, .. parts]);

    [Fact]
    public void DefaultOutput_IsBesideInputWithCodecSuffix()
    {
        Assert.Equal(PathOf("movie.hevc.mkv"), OutputNamer.Resolve(PathOf("movie.mkv"), null, null, Hevc, treatOutputAsDirectory: false));
        Assert.Equal(PathOf("movie.remux.mp4"), OutputNamer.Resolve(PathOf("movie.mp4"), null, null, Copy, treatOutputAsDirectory: false));
    }

    [Fact]
    public void Format_OverridesExtension()
    {
        Assert.Equal(PathOf("movie.hevc.mp4"), OutputNamer.Resolve(PathOf("movie.mkv"), null, "mp4", Hevc, false));
        Assert.Equal(PathOf("movie.hevc.webm"), OutputNamer.Resolve(PathOf("movie.mkv"), null, ".WEBM", Hevc, false));
    }

    [Fact]
    public void UnwritableInputContainer_FallsBackToMkv()
    {
        Assert.Equal(PathOf("clip.hevc.mkv"), OutputNamer.Resolve(PathOf("clip.avi"), null, null, Hevc, false));
    }

    [Fact]
    public void ExplicitFile_IsUsedAsIs()
    {
        Assert.Equal("out/x.mp4", OutputNamer.Resolve(PathOf("movie.mkv"), "out/x.mp4", null, Hevc, false));
    }

    [Fact]
    public void OutputDirectory_DropsSuffixUnlessItIsTheInputDirectory()
    {
        var target = PathOf("encoded") + Path.DirectorySeparatorChar;

        Assert.Equal(PathOf("encoded", "movie.mkv"), OutputNamer.Resolve(PathOf("movie.mkv"), target, null, Hevc, false));
        Assert.Equal(PathOf("movie.hevc.mkv"), OutputNamer.Resolve(PathOf("movie.mkv"), _directory, null, Hevc, treatOutputAsDirectory: true));
    }

    [Fact]
    public void UrlInput_NamesFromPathWithoutQuery()
    {
        var resolved = OutputNamer.Resolve("https://cdn.example/show/episode.mp4?token=abc", _directory, null, Hevc, true);

        Assert.Equal(PathOf("episode.mp4"), resolved);
    }

    [Fact]
    public void Directories_ExpandToSortedMediaFilesOnly()
    {
        foreach (var name in new[] { "b.mkv", "A.mp4", "notes.txt", ".hidden.mkv", "c.MOV" })
        {
            File.WriteAllText(PathOf(name), "x");
        }

        Directory.CreateDirectory(PathOf("sub"));
        File.WriteAllText(PathOf("sub", "deep.mkv"), "x");

        var (inputs, empty) = InputExpander.Expand([_directory]);

        Assert.Equal([PathOf("A.mp4"), PathOf("b.mkv"), PathOf("c.MOV")], inputs);
        Assert.Empty(empty);
    }

    [Fact]
    public void Expansion_DeduplicatesAndPassesThroughMissingAndUrls()
    {
        File.WriteAllText(PathOf("a.mkv"), "x");
        Directory.CreateDirectory(PathOf("empty"));

        var (inputs, empty) = InputExpander.Expand([PathOf("a.mkv"), _directory, PathOf("missing.mkv"), "https://x/y.mp4", PathOf("empty")]);

        Assert.Equal([PathOf("a.mkv"), PathOf("missing.mkv"), "https://x/y.mp4"], inputs);
        Assert.Equal([PathOf("empty")], empty);
    }
}
