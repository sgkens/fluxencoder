using System.Text.Json;
using System.Xml.Linq;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Core.Manifests;
using FluxEncoder.Infrastructure.Manifests;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Storage;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Cli.Tests.Commands;

/// <summary>
/// <c>info</c> against a real file whose title is hostile in every output format.
/// </summary>
public sealed class InfoCommandTests : IAsyncLifetime
{
    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);

    private const string HostileTitle = "<script>alert(1)</script> | **x** no";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-info-" + Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        if (Tools.IsComplete)
        {
            var output = await ProcessCapture.RunAsync(
                Tools.FFmpeg!.Path,
                ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=1", "-f", "lavfi", "-i", "sine=duration=1",
                    "-metadata", $"title={HostileTitle}\u001b[31m", "-metadata", "location=+48.8577+002.2950/", "-c:v", "libx264", "-c:a", "aac", Clip],
                TimeSpan.FromSeconds(60));
            Assert.True(output.Succeeded, output.StandardError);
        }
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private string Clip => Path.Combine(_directory, "clip.mkv");

    private sealed record Run(int ExitCode, string Out, string Error);

    private async Task<Run> InfoAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var paths = new AppPaths(Path.Combine(_directory, "config"), Path.Combine(_directory, "data"), Path.Combine(_directory, "cache"));
        var host = new CliHost(output, error, _ => null, ErrorIsTerminal: false) { Paths = paths };

        var exitCode = await CliApplication.RunAsync(["info", .. args], host, TestContext.Current.CancellationToken);
        return new Run(exitCode, output.ToString(), error.ToString());
    }

    private static void SkipWithoutFFmpeg() => Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");

    [Fact]
    public async Task Text_NeutralizesControlCharactersAndHidesLocation()
    {
        SkipWithoutFFmpeg();

        var run = await InfoAsync(Clip);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(HostileTitle + "?[31m", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("48.8577", run.Out, StringComparison.Ordinal);
        Assert.Contains("1 location or device tag is hidden", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_And_Yaml_RoundTripTheHostileTitle()
    {
        SkipWithoutFFmpeg();

        var json = await InfoAsync(Clip, "--format", "json");
        using var document = JsonDocument.Parse(json.Out);
        Assert.Equal(HostileTitle + "\u001b[31m", document.RootElement.GetProperty("tags").GetProperty("title").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("streams").GetArrayLength());

        var yaml = await InfoAsync(Clip, "--format", "yaml");
        var (root, error) = ManifestParser.ParseYaml(yaml.Out);
        Assert.Null(error);
        var tags = (ManifestMapping)((ManifestMapping)root!).Entries.Single(e => e.Key.Value == "tags").Value;
        Assert.Equal(HostileTitle + "\u001b[31m", ((ManifestScalar)tags.Entries.Single(e => e.Key.Value == "title").Value).Value);
    }

    [Fact]
    public async Task Exports_EncodeForTheirFormat()
    {
        SkipWithoutFFmpeg();

        var markdown = (await InfoAsync(Clip, "-f", "markdown")).Out;
        Assert.Contains("\\<script\\>alert\\(1\\)\\</script\\> \\| \\*\\*x\\*\\* no\\[31m", markdown, StringComparison.Ordinal);

        var html = (await InfoAsync(Clip, "-f", "html")).Out;
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);

        var svg = (await InfoAsync(Clip, "-f", "svg")).Out;
        Assert.Equal("svg", XDocument.Parse(svg).Root!.Name.LocalName);
    }

    [Fact]
    public async Task ShowPrivate_IncludesLocation()
    {
        SkipWithoutFFmpeg();

        Assert.Contains("48.8577", (await InfoAsync(Clip, "--show-private")).Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingInput_IsInputError()
    {
        SkipWithoutFFmpeg();

        var run = await InfoAsync(Path.Combine(_directory, "missing.mkv"));

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("does not exist", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownFormat_IsUsageError()
    {
        Assert.Equal(2, (await InfoAsync(Clip, "-f", "pdf")).ExitCode);
    }
}
