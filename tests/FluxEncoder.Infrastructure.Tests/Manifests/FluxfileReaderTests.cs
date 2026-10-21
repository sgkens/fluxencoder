using System.Collections.Immutable;
using FluxEncoder.Core.Manifests;
using FluxEncoder.Infrastructure.Manifests;

namespace FluxEncoder.Infrastructure.Tests.Manifests;

public sealed class FluxfileReaderTests
{
    private static readonly string Directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "flux-manifest"));

    private static readonly ImmutableDictionary<string, FluxOptionKind> Options = new Dictionary<string, FluxOptionKind>
    {
        ["codec"] = FluxOptionKind.Value,
        ["quality"] = FluxOptionKind.Value,
        ["audio"] = FluxOptionKind.Value,
        ["two-pass"] = FluxOptionKind.Flag,
        ["no-chapters"] = FluxOptionKind.Flag,
    }.ToImmutableDictionary();

    private static (Fluxfile? File, ImmutableArray<ManifestError> Errors) Read(string yaml, Func<string, string?>? variables = null, bool allowOutside = false)
    {
        var (root, error) = ManifestParser.ParseYaml(yaml);
        Assert.Null(error);
        return FluxfileReader.Read(root!, new FluxfileReadOptions
        {
            Path = Path.Combine(Directory, "Fluxfile.yaml"),
            EncodeOptions = Options,
            RefusedOptions = ImmutableDictionary<string, string>.Empty.Add("ffmpeg-arg", "raw FFmpeg arguments are not allowed."),
            Variables = variables ?? (_ => null),
            AllowOutputsOutside = allowOutside,
        });
    }

    private static Fluxfile Valid(string yaml, Func<string, string?>? variables = null)
    {
        var (file, errors) = Read(yaml, variables);
        Assert.True(errors.IsEmpty, string.Join("\n", errors.Select(e => e.Format("Fluxfile.yaml"))));
        return file!;
    }

    private static string Errors(string yaml, bool allowOutside = false)
    {
        var (file, errors) = Read(yaml, allowOutside: allowOutside);
        Assert.Null(file);
        return string.Join("\n", errors.Select(e => e.Format("Fluxfile.yaml")));
    }

    [Fact]
    public void Reads_JobsWithDefaultsPathsAndReferences()
    {
        var file = Valid("""
            version: 1
            defaults:
              codec: hevc
              quality: high
              no-chapters: true
            jobs:
              master:
                input: raw/take1.mov
                output: out/master.mkv
                quality: balanced
                no-chapters: false
              web:
                input: "@master"
                output: out/web/
                codec: h264
                audio: [jpn, eng]
                two-pass: true
              remote:
                input: [https://cdn.example/a.mkv, remote://nas/b.mkv]
            """);

        Assert.Equal(1, file.Version);
        var master = file.Jobs[0];
        Assert.Equal(Path.Combine(Directory, "raw", "take1.mov"), master.Inputs.Single().Path);
        Assert.Equal(Path.Combine(Directory, "out", "master.mkv"), master.Output);
        Assert.Equal([new FluxOption("codec", "hevc"), new FluxOption("quality", "balanced")], master.Options);

        var web = file.Jobs[1];
        Assert.Equal("master", web.Inputs.Single().FromJob);
        Assert.Equal(["master"], web.Needs);
        Assert.Equal(Path.Combine(Directory, "out", "web") + Path.DirectorySeparatorChar, web.Output);
        Assert.Equal(
            [new FluxOption("quality", "high"), new FluxOption("no-chapters", null), new FluxOption("codec", "h264"), new FluxOption("audio", "jpn,eng"), new FluxOption("two-pass", null)],
            web.Options);

        Assert.Equal(["https://cdn.example/a.mkv", "remote://nas/b.mkv"], file.Jobs[2].Inputs.Select(i => i.Path));
        Assert.Null(file.Jobs[2].Output);
    }

    [Fact]
    public void Substitutes_Variables()
    {
        var file = Valid(
            "version: 1\njobs:\n  a:\n    input: ${FLUX_SRC}/a.mov\n    output: ${OUT:-out}/a.mkv\n    quality: ${FLUX_Q}\n",
            name => name switch { "FLUX_SRC" => "/media", "FLUX_Q" => "high", _ => null });

        Assert.Equal(Path.GetFullPath("/media/a.mov"), file.Jobs[0].Inputs[0].Path);
        Assert.Equal(Path.Combine(Directory, "out", "a.mkv"), file.Jobs[0].Output);
        Assert.Equal("high", file.Jobs[0].Options.Single().Value);
    }

    [Theory]
    [InlineData("jobs:\n  a:\n    input: a.mov\n", "'version' is required")]
    [InlineData("version: 2\njobs:\n  a:\n    input: a.mov\n", "version 2 is newer")]
    [InlineData("version: one\njobs:\n  a:\n    input: a.mov\n", "'version' must be a whole number")]
    [InlineData("version: 1\n", "'jobs' is required")]
    [InlineData("version: 1\njobs: {}\n", "'jobs' is empty")]
    [InlineData("version: 1\njobs: []\n", "'jobs' must be a mapping")]
    [InlineData("version: 1\njob:\n  a:\n    input: a.mov\n", "Unknown top-level key 'job'; did you mean 'jobs'?")]
    [InlineData("version: 1\njobs:\n  a:\n    input: a.mov\n    codex: hevc\n", "5:5: Unknown job key or encode option 'codex'; did you mean 'codec'?")]
    [InlineData("version: 1\ndefaults:\n  qualty: high\njobs:\n  a:\n    input: a.mov\n", "Unknown encode option 'qualty'; did you mean 'quality'?")]
    [InlineData("version: 1\njobs:\n  a:\n    input: a.mov\n    ffmpeg-arg: -y\n", "'ffmpeg-arg' cannot be set in a Fluxfile: raw FFmpeg arguments are not allowed.")]
    [InlineData("version: 1\njobs:\n  a:\n    output: a.mkv\n", "Job 'a' has no 'input'")]
    [InlineData("version: 1\njobs:\n  a:\n    input: ~\n", "'input' has no value")]
    [InlineData("version: 1\njobs:\n  a:\n    input: a.mov\n    two-pass: yes\n", "'two-pass' takes true or false, not 'yes'")]
    [InlineData("version: 1\njobs:\n  a:\n    input: a.mov\n    codec: {x: 1}\n", "'codec' must be a single value")]
    [InlineData("version: 1\njobs:\n  bad name:\n    input: a.mov\n", "Job name 'bad name' is not valid")]
    [InlineData("version: 1\njobs:\n  a:\n    input: a.mov\n  A:\n    input: b.mov\n", "Job names are case-insensitive")]
    [InlineData("version: 1\njobs:\n  a:\n    input: ${FLUX_NOPE}/a.mov\n", "4:12: Variable 'FLUX_NOPE' is not set")]
    public void Refuses_InvalidManifests(string yaml, string expected)
    {
        Assert.Contains(expected, Errors(yaml), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("version: 1\njobs:\n  a:\n    input: x.mov\n    needs: [ghost]\n", "Job 'a' needs 'ghost', which is not a job here.")]
    [InlineData("version: 1\njobs:\n  a:\n    input: \"@a\"\n", "Job 'a' cannot need itself.")]
    [InlineData("version: 1\njobs:\n  a:\n    input: \"@b\"\n  b:\n    input: \"@c\"\n  c:\n    input: \"@a\"\n", "cycle: a → b → c → a")]
    [InlineData("version: 1\njobs:\n  a:\n    input: x.mov\n    output: o.mkv\n  b:\n    input: y.mov\n    output: ./o.mkv\n", "Jobs 'a' and 'b' both write")]
    [InlineData("version: 1\njobs:\n  a:\n    input: x.mkv\n    output: x.mkv\n", "would write over its own input")]
    [InlineData("version: 1\njobs:\n  a:\n    input: x.mov\n    output: https://example/x.mkv\n", "'output' must be a local file or directory")]
    public void Refuses_InvalidJobGraphsAndOutputs(string yaml, string expected)
    {
        Assert.Contains(expected, Errors(yaml), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../elsewhere/a.mkv")]
    [InlineData("/etc/a.mkv")]
    [InlineData(".")]
    public void Outputs_StayInsideTheManifestDirectory(string output)
    {
        var yaml = $"version: 1\njobs:\n  a:\n    input: x.mov\n    output: {output}\n";

        Assert.Contains("outside the Fluxfile's directory", Errors(yaml), StringComparison.Ordinal);
        Assert.Empty(Read(yaml, allowOutside: true).Errors);
    }

    [Fact]
    public void Reports_EveryProblemAtOnce()
    {
        var errors = Errors("version: 3\njobs:\n  a:\n    input: x.mov\n    codex: 1\n  b:\n    needs: [zzz]\n");

        Assert.Equal(4, errors.Split('\n').Length);
    }

    [Fact]
    public void ExecutionOrder_RunsNeedsFirstAndSelectsTransitively()
    {
        var file = Valid("""
            version: 1
            jobs:
              web:
                input: "@master"
              master:
                input: "@proxy"
              proxy:
                input: a.mov
              other:
                input: b.mov
            """);

        Assert.Equal(["proxy", "master", "web", "other"], file.ExecutionOrder().Select(j => j.Name));
        Assert.Equal(["proxy", "master", "web"], file.ExecutionOrder(["WEB"]).Select(j => j.Name));
        Assert.Throws<ArgumentException>(() => file.ExecutionOrder(["nope"]));
    }
}
