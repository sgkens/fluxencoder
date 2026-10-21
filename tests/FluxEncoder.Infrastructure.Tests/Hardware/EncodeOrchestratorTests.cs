using System.Diagnostics.CodeAnalysis;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using FluxEncoder.Infrastructure.Execution;
using FluxEncoder.Infrastructure.Planning;
using FluxEncoder.Infrastructure.Processes;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Tests.Hardware;

/// <summary>
/// Hardware selection and CPU fallback end to end (TSKE006.3, TSKE006.5), using Debian's FFmpeg, whose NVENC is
/// listed but cannot run without an NVIDIA GPU: a real driver failure, not a simulated one.
/// </summary>
public sealed class EncodeOrchestratorTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-hw-" + Guid.NewGuid().ToString("N"));
    private FFmpegCapabilities _capabilities = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        if (HardwareHosts.DistributionFFmpeg is not { } tools || HardwareHosts.ExpectedVendor == "nvidia")
        {
            return;
        }

        _capabilities = (await FFmpegCapabilityProbe.ProbeAsync(tools.FFmpeg)).Value!;
        var generated = await ProcessCapture.RunAsync(
            tools.FFmpeg,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=1", "-c:v", "libx264", Path.Combine(_directory, "in.mkv")],
            TimeSpan.FromSeconds(60));
        Assert.True(generated.Succeeded, generated.StandardError);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private EncodeEnvironment Environment(HardwareRegistry? registry = null)
    {
        Assert.SkipUnless(HardwareHosts.DistributionFFmpeg is not null, "Debian FFmpeg (/usr/bin/ffmpeg) is not installed.");
        Assert.SkipWhen(HardwareHosts.ExpectedVendor == "nvidia", "These tests rely on NVENC failing.");
        Assert.SkipUnless(_capabilities.HasEncoder("h264_nvenc"), "This FFmpeg build does not list NVENC.");

        var (ffmpeg, ffprobe) = HardwareHosts.DistributionFFmpeg!.Value;
        return new EncodeEnvironment
        {
            FFmpegPath = ffmpeg,
            FFprobePath = ffprobe,
            Capabilities = _capabilities,
            FileSystem = LocalPlanningFileSystem.Instance,
            ScratchRoot = _directory,
            Registry = registry ?? new HardwareRegistry(new CpuStrategy(), [new NvencStrategy()]),
        };
    }

    private EncodingJob Job(string output, HardwareVendor? hardware = null, bool fallback = false) => new()
    {
        InputPath = Path.Combine(_directory, "in.mkv"),
        OutputPath = Path.Combine(_directory, output),
        Video = new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Fast),
        Audio = new AudioTarget.StreamCopy(),
        Hardware = hardware,
        HardwareFallback = fallback,
    };

    [Fact]
    public async Task Auto_ProbesNvencFindsItUnusableAndEncodesOnCpu()
    {
        var outcome = await EncodeOrchestrator.RunAsync(Job("auto.mkv"), Environment(), cancellationToken: Token);

        Assert.Equal(JobOutcome.Succeeded, outcome.Result.Outcome);
        Assert.Equal(HardwareVendor.Cpu, outcome.Strategy!.Vendor);
        Assert.Equal(EncoderStatus.Unavailable, Assert.Single(outcome.Probes).Status);
        Assert.Empty(outcome.Result.Warnings);
    }

    [Fact]
    public async Task ForcedUnusableVendor_FailsWithExitCode6AndHint()
    {
        var outcome = await EncodeOrchestrator.RunAsync(Job("forced.mkv", HardwareVendor.Nvidia), Environment(), cancellationToken: Token);

        Assert.Equal(FailureCategory.HardwareUnavailable, outcome.Result.Failure?.Category);
        Assert.Equal(ExitCode.Unsupported, outcome.Result.ExitCode);
        Assert.Contains("--hardware-fallback", outcome.Result.Failure!.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_directory, "forced.mkv")));
    }

    [Fact]
    public async Task ForcedUnusableVendorWithFallback_EncodesOnCpuAndWarns()
    {
        var outcome = await EncodeOrchestrator.RunAsync(Job("fallback.mkv", HardwareVendor.Nvidia, fallback: true), Environment(), cancellationToken: Token);

        Assert.Equal(JobOutcome.Succeeded, outcome.Result.Outcome);
        Assert.Equal(WarningCodes.HardwareFallback, Assert.Single(outcome.Result.Warnings).Code);
        Assert.True(File.Exists(Path.Combine(_directory, "fallback.mkv")));
    }

    [Fact]
    public async Task HardwareFailingMidEncode_IsRetriedOnCpu()
    {
        // Encodes like the CPU (so the probe, which decodes nothing, passes) but decodes with CUDA, which fails on
        // the real input exactly as a driver failure would.
        var registry = new HardwareRegistry(new CpuStrategy(), [new CudaDecodeStrategy()]);

        var outcome = await EncodeOrchestrator.RunAsync(Job("runtime.mkv"), Environment(registry), cancellationToken: Token);

        Assert.True(Assert.Single(outcome.Probes).IsAvailable);
        Assert.Equal(JobOutcome.Succeeded, outcome.Result.Outcome);
        Assert.Equal(HardwareVendor.Cpu, outcome.Strategy!.Vendor);
        var warning = Assert.Single(outcome.Result.Warnings, w => w.Code == WarningCodes.HardwareFallback);
        Assert.Contains("libcuda", warning.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_directory, "runtime.mkv")));
        Assert.Empty(Directory.GetFiles(_directory, ".*partial*"));
    }

    [Fact]
    public async Task StreamCopy_ProbesNothing()
    {
        var job = Job("copy.mkv", HardwareVendor.Nvidia) with { Video = new VideoTarget.StreamCopy() };

        var outcome = await EncodeOrchestrator.RunAsync(job, Environment(), cancellationToken: Token);

        Assert.True(outcome.Result.Outcome == JobOutcome.Succeeded, $"{outcome.Result.Failure?.Message}\n{outcome.Result.Failure?.Detail}\n{outcome.Planned?.Plan?.Invocations[^1].ToDisplayString()}");
        Assert.Empty(outcome.Probes);
    }

    private sealed class CudaDecodeStrategy : IHardwareStrategy
    {
        private readonly CpuStrategy _cpu = new();

        public HardwareVendor Vendor => HardwareVendor.Nvidia;

        public string DisplayName => "Test CUDA";

        public IReadOnlyList<string> DecodeArguments => ["-hwaccel", "cuda"];

        public bool TryGetVideoEncoder(VideoCodec codec, [NotNullWhen(true)] out string? encoder) => _cpu.TryGetVideoEncoder(codec, out encoder);

        public IReadOnlyList<KeyValuePair<string, string>> GetQualityOptions(VideoCodec codec, QualityPreset quality) => _cpu.GetQualityOptions(codec, quality);

        public IReadOnlyList<KeyValuePair<string, string>> GetBitRateOptions(VideoCodec codec, QualityPreset quality) => _cpu.GetBitRateOptions(codec, quality);

        public bool SupportsTwoPass(VideoCodec codec) => false;

        public string? GetPixelFormat(VideoCodec codec, bool highBitDepth) => _cpu.GetPixelFormat(codec, highBitDepth);
    }
}
