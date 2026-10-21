using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using FluxEncoder.Infrastructure.Probing;
using FluxEncoder.Infrastructure.Tooling;
using FluxEncoder.Infrastructure.Tests.Support;

namespace FluxEncoder.Infrastructure.Tests.Tooling;

public sealed class FFmpegCapabilityTests
{
    private static string Listing(string name) => Fixture.Read("ffmpeg", name + ".txt");

    private static FFmpegCapabilities Fixture801()
    {
        var result = FFmpegCapabilityProbe.Parse(
            Listing("version"), Listing("encoders"), Listing("decoders"), Listing("muxers"), Listing("demuxers"),
            Listing("protocols"), Listing("filters"), Listing("hwaccels"), "/usr/bin/ffmpeg");

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void Version_AndConfiguration()
    {
        var caps = Fixture801();

        Assert.Equal("8.0.1", caps.Version.Raw);
        Assert.Equal((8, 0, 1), (caps.Version.Major!.Value, caps.Version.Minor!.Value, caps.Version.Patch!.Value));
        Assert.True(caps.IsEnabled("--enable-libx265"));
        Assert.True(caps.IsEnabled("--enable-gpl"));
        Assert.All(caps.Configuration, flag => Assert.StartsWith("--", flag));
    }

    [Fact]
    public void Encoders_WithKindCodecAndExperimentalFlag()
    {
        var caps = Fixture801();

        var x265 = caps.Encoders["libx265"];
        Assert.Equal(MediaStreamKind.Video, x265.Kind);
        Assert.Equal("hevc", x265.CodecName);
        Assert.Equal("libx265 H.265 / HEVC", x265.Description);

        var aac = caps.Encoders["aac"];
        Assert.Equal(MediaStreamKind.Audio, aac.Kind);
        Assert.Null(aac.CodecName);
        Assert.Equal("aac", aac.Codec);

        Assert.Equal(MediaStreamKind.Subtitle, caps.Encoders["srt"].Kind);
        Assert.Contains(caps.Encoders.Values, e => e.IsExperimental);
        Assert.False(x265.IsExperimental);
        Assert.Contains("libx265", caps.EncodersFor("hevc").Select(e => e.Name));
        Assert.DoesNotContain(caps.Encoders.Keys, k => k.Contains(' ') || k.Length == 0);
    }

    [Fact]
    public void Decoders_AreParsed()
    {
        var caps = Fixture801();

        Assert.True(caps.HasDecoder("hevc"));
        Assert.True(caps.HasDecoder("h264"));
        Assert.True(caps.Decoders.Count > 100);
    }

    [Fact]
    public void Formats_SplitAliasesAndMarkDevices()
    {
        var caps = Fixture801();

        var matroska = Assert.Single(caps.Demuxers, d => d.Names.Contains("webm"));
        Assert.Equal(["matroska", "webm"], matroska.Names);
        Assert.Equal("Matroska / WebM", matroska.Description);

        var mov = Assert.Single(caps.Demuxers, d => d.Names.Contains("mp4"));
        Assert.Equal("mov", mov.Name);

        Assert.Contains(caps.Muxers, m => m.Name == "matroska");
        Assert.Contains(caps.Muxers, m => m.Name == "mp4");
        Assert.DoesNotContain(caps.Muxers, m => m.Names.Any(n => n.Contains(' ')));
    }

    [Fact]
    public void ProbedFormatNames_ResolveAgainstDemuxerCatalog()
    {
        var caps = Fixture801();
        var mkv = FFprobeJsonParser.Parse(Fixture.Read("ffprobe", "hdr10-multitrack.mkv.json"), "a.mkv");
        var mp4 = FFprobeJsonParser.Parse(Fixture.Read("ffprobe", "hlg-coverart.mp4.json"), "a.mp4");

        Assert.True(caps.Demuxers.TryFind(string.Join(',', mkv.FormatNames), out var mkvEntry));
        Assert.Equal("matroska", mkvEntry.Name);
        Assert.True(caps.Demuxers.AreSameFormat(mp4.FormatNames[1], "mov"));

        // Muxers list matroska and webm separately, unlike the demuxer.
        Assert.False(caps.Muxers.AreSameFormat("matroska", "webm"));
        Assert.True(caps.Muxers.Contains("webm"));
    }

    [Fact]
    public void Protocols_SplitInputAndOutput()
    {
        var caps = Fixture801();

        Assert.Contains("file", (IReadOnlySet<string>)caps.InputProtocols);
        Assert.Contains("http", (IReadOnlySet<string>)caps.InputProtocols);
        Assert.Contains("file", (IReadOnlySet<string>)caps.OutputProtocols);
        Assert.DoesNotContain("Input:", (IReadOnlySet<string>)caps.InputProtocols);
        Assert.DoesNotContain("async", (IReadOnlySet<string>)caps.OutputProtocols);
    }

    [Fact]
    public void Filters_AreParsedWithoutLegend()
    {
        var caps = Fixture801();

        Assert.True(caps.HasFilter("scale"));
        Assert.True(caps.HasFilter("zscale") || caps.HasFilter("tonemap"));
        Assert.True(caps.HasFilter("aap"));
        Assert.DoesNotContain("=", (IReadOnlySet<string>)caps.Filters);
        Assert.True(caps.Filters.Count > 300);
    }

    [Fact]
    public void HardwareAccelerators_EmptyListingIsEmpty()
    {
        Assert.Empty(Fixture801().HardwareAccelerators);
    }

    [Fact]
    public void HardwareAccelerators_ListedMethods()
    {
        const string Text = "Hardware acceleration methods:\ncuda\nvaapi\r\nqsv\n\ndrm\n";

        Assert.Equal(["cuda", "vaapi", "qsv", "drm"], FFmpegListingParser.ParseHardwareAccelerators(Text));
    }

    [Theory]
    [InlineData("8.0.1", 8, 0, 1)]
    [InlineData("n7.1", 7, 1, null)]
    [InlineData("6.1.1-3ubuntu5", 6, 1, 1)]
    [InlineData("7.1-full_build-www.gyan.dev", 7, 1, null)]
    [InlineData("N-118000-gabc1234-20250101", null, null, null)]
    [InlineData("2024-12-01-git-abc-essentials_build-www.gyan.dev", null, null, null)]
    [InlineData("git-2024", null, null, null)]
    public void Version_ReleaseNumbersWhenPresent(string raw, int? major, int? minor, int? patch)
    {
        var version = new FFmpegVersion(raw);

        Assert.Equal(raw, version.Raw);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Fact]
    public void NotFFmpeg_IsDependencyFailure()
    {
        var result = FFmpegCapabilityProbe.Parse("hello", "", "", "", "", "", "", "", "/usr/bin/notffmpeg");

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureCategory.DependencyMissing, result.Failure.Category);
    }

    [Fact]
    public async Task RealFFmpeg_ProducesSnapshot()
    {
        var discovery = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);
        Assert.SkipUnless(discovery.FFmpeg is not null, "FFmpeg is not installed on this machine.");

        var result = await FFmpegCapabilityProbe.ProbeAsync(discovery.FFmpeg.Path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.NotEmpty(result.Value.Encoders);
        Assert.NotEmpty(result.Value.Muxers);
        Assert.Contains("file", (IReadOnlySet<string>)result.Value.InputProtocols);
    }

    [Fact]
    public async Task MissingExecutable_IsDependencyFailure()
    {
        var result = await FFmpegCapabilityProbe.ProbeAsync(
            Path.Combine(Path.GetTempPath(), "no-ffmpeg-" + Guid.NewGuid().ToString("N")),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(FailureCategory.DependencyMissing, result.Failure?.Category);
    }
}
