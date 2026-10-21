using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Probing;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Tests.Probing;

/// <summary>
/// Runs the real FFprobe against small media generated with FFmpeg's lavfi sources. Skipped when FFmpeg is
/// not installed.
/// </summary>
public sealed class MediaProberTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-prober-" + Guid.NewGuid().ToString("N"));
    private ToolDiscovery _tools = null!;

    public async ValueTask InitializeAsync()
    {
        _tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);
        Directory.CreateDirectory(_directory);

        if (_tools.IsComplete)
        {
            await GenerateAsync("clip.mkv",
                ["-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=1", "-f", "lavfi", "-i", "sine=duration=1",
                 "-c:v", "libx264", "-c:a", "aac", "-metadata:s:a:0", "language=eng"]);
            await GenerateAsync("hdr10.mp4",
                ["-f", "lavfi", "-i", "testsrc2=size=160x120:rate=25:duration=1", "-c:v", "libx265", "-pix_fmt", "yuv420p10le",
                 "-x265-params", "log-level=error:hdr10=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400"]);
        }
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private async Task GenerateAsync(string name, string[] arguments)
    {
        var output = await ProcessCapture.RunAsync(
            _tools.FFmpeg!.Path,
            ["-hide_banner", "-loglevel", "error", "-y", .. arguments, Path.Combine(_directory, name)],
            TimeSpan.FromSeconds(60));

        Assert.True(output.Succeeded, output.StandardError);
    }

    private string FFprobe
    {
        get
        {
            Assert.SkipUnless(_tools.IsComplete, "FFmpeg is not installed on this machine.");
            return _tools.FFprobe!.Path;
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ValidFile_IsProbedAndValidated()
    {
        var result = await MediaProber.ProbeAsync(FFprobe, Path.Combine(_directory, "clip.mkv"), Token);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.True(result.Value.IsProbed);
        Assert.Equal(InputKind.LocalFile, result.Value.Source.Kind);
        Assert.Equal("h264", result.Value.Media.MotionVideoStreams.Single().CodecName);
        Assert.Equal("eng", result.Value.Media.AudioStreams.Single().Language?.Value);
        Assert.True(result.Value.Media.HasKnownDuration);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task Hdr10Mp4_ReadsFrameMasteringMetadata()
    {
        var result = await MediaProber.ProbeAsync(FFprobe, Path.Combine(_directory, "hdr10.mp4"), Token);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        var video = result.Value.Media.MotionVideoStreams.Single();
        Assert.Equal(HdrFormat.Hdr10, video.HdrFormats);
        Assert.NotNull(video.Hdr.MasteringDisplay);
        Assert.Equal(new ContentLightLevel(1000, 400), video.Hdr.ContentLightLevel);
    }

    [Fact]
    public async Task MissingFile_IsInputNotFound()
    {
        var result = await MediaProber.ProbeAsync(FFprobe, Path.Combine(_directory, "missing.mkv"), Token);

        Assert.Equal(FailureCategory.InputNotFound, result.Failure?.Category);
        Assert.Equal(ExitCode.InputError, result.Failure!.ExitCode);
    }

    [Fact]
    public async Task Directory_IsInvalidInput()
    {
        var result = await MediaProber.ProbeAsync(FFprobe, _directory, Token);

        Assert.Equal(FailureCategory.InvalidInput, result.Failure?.Category);
    }

    [Fact]
    public async Task CorruptFile_IsInvalidInputWithDetail()
    {
        var path = Path.Combine(_directory, "corrupt.mkv");
        await File.WriteAllTextAsync(path, "this is not media", Token);

        var result = await MediaProber.ProbeAsync(FFprobe, path, Token);

        Assert.Equal(FailureCategory.InvalidInput, result.Failure?.Category);
        Assert.Contains("Invalid data", result.Failure!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyFile_IsInvalidInput()
    {
        var path = Path.Combine(_directory, "empty.mp4");
        await File.WriteAllBytesAsync(path, [], Token);

        var result = await MediaProber.ProbeAsync(FFprobe, path, Token);

        Assert.Equal(FailureCategory.InvalidInput, result.Failure?.Category);
    }

    [Fact]
    public async Task InputStartingWithDash_IsNotReadAsOption()
    {
        var path = Path.Combine(_directory, "-dash.mkv");
        File.Copy(Path.Combine(_directory, "clip.mkv"), path);

        var result = await MediaProber.ProbeAsync(FFprobe, path, Token);

        Assert.True(result.IsSuccess, result.Failure?.Message);
    }

    [Fact]
    public async Task RefusedConnection_IsTransientNetworkError()
    {
        // Port 9 (discard) is closed on a typical host, so the connection is refused immediately.
        var result = await MediaProber.ProbeAsync(FFprobe, "http://127.0.0.1:9/stream.ts", Token);

        Assert.Equal(FailureCategory.NetworkError, result.Failure?.Category);
        Assert.True(result.Failure!.IsTransient);
    }

    [Fact]
    public async Task Pipe_IsNotProbed()
    {
        var result = await MediaProber.ProbeAsync("/nonexistent/ffprobe", "pipe:0", Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsProbed);
        Assert.Empty(result.Value.Media.Streams);
        Assert.Equal(WarningCodes.InputNotProbed, Assert.Single(result.Value.Warnings).Code);
    }

    [Fact]
    public async Task MissingFFprobe_IsDependencyFailure()
    {
        var result = await MediaProber.ProbeAsync(Path.Combine(_directory, "no-ffprobe"), Path.Combine(_directory, "clip.mkv"), Token);

        Assert.Equal(FailureCategory.DependencyMissing, result.Failure?.Category);
    }

    [Fact]
    public void NetworkArguments_IncludeIoTimeoutBeforeInput()
    {
        var arguments = MediaProber.MediaArguments(InputSource.Classify("https://example.com/a.mp4"));

        var timeout = arguments.ToList().IndexOf("-rw_timeout");
        Assert.True(timeout >= 0);
        Assert.Equal("15000000", arguments[timeout + 1]);
        Assert.Equal(["-i", "https://example.com/a.mp4"], arguments.TakeLast(2));
        Assert.DoesNotContain("-rw_timeout", MediaProber.MediaArguments(InputSource.Classify("a.mkv")));
    }

    [Fact]
    public void ProtocolLikeFileName_IsProbedAsAbsoluteLocalPath()
    {
        // FFmpeg would open a relative "http:evil.mkv" as an HTTP URL to the host "evil.mkv".
        var arguments = MediaProber.MediaArguments(InputSource.Classify("http:evil.mkv"));

        Assert.Equal(["-protocol_whitelist", "file,crypto,data", "-i", Path.GetFullPath("http:evil.mkv")], arguments.TakeLast(4));
    }

    [Fact]
    public async Task DeniedScheme_IsRefusedWithoutRunningFFprobe()
    {
        var policy = Core.Security.SourcePolicy.Default with { AllowedSchemes = ["https"] };

        var result = await MediaProber.ProbeAsync("/nonexistent/ffprobe", "http://127.0.0.1:9/x.ts", Token, policy);

        Assert.Equal(FailureCategory.PolicyDenied, result.Failure?.Category);
    }
}
