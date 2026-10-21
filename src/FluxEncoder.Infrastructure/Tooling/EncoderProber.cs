using System.ComponentModel;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Tooling;
using FluxEncoder.Infrastructure.Execution;
using FluxEncoder.Infrastructure.Processes;

namespace FluxEncoder.Infrastructure.Tooling;

/// <summary>
/// Establishes whether a hardware encoder actually works on this machine by encoding a few generated frames to
/// FFmpeg's null output (TSKE006.2). Being listed in <c>ffmpeg -encoders</c> only means the encoder was compiled in.
/// </summary>
/// <remarks>
/// <para>
/// A probe succeeds only when FFmpeg exits with code 0 <em>and</em> writes nothing at <c>-loglevel error</c>.
/// The exit code alone is not trusted: the Windows FFmpeg 7.1 build exits 0 when AMF refuses a 10-bit encode.
/// </para>
/// <para>
/// The test uses the same quality options as a real encode, so an option the device rejects is caught here too.
/// No files are read or written, which also lets a Windows FFmpeg be probed from WSL.
/// </para>
/// </remarks>
public static class EncoderProber
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task<IReadOnlyList<EncoderProbe>> ProbeAllAsync(
        string ffmpegPath, IEnumerable<IHardwareStrategy> strategies, VideoCodec codec, FFmpegCapabilities capabilities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategies);

        // Sequential: some drivers limit concurrent sessions, and a probe takes well under a second.
        var probes = new List<EncoderProbe>();
        foreach (var strategy in strategies)
        {
            probes.Add(await ProbeAsync(ffmpegPath, strategy, codec, capabilities, cancellationToken).ConfigureAwait(false));
        }

        return probes;
    }

    public static async Task<EncoderProbe> ProbeAsync(
        string ffmpegPath, IHardwareStrategy strategy, VideoCodec codec, FFmpegCapabilities capabilities, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (!strategy.TryGetVideoEncoder(codec, out var encoder) || !capabilities.HasEncoder(encoder))
        {
            return new EncoderProbe(strategy.Vendor, codec, encoder ?? string.Empty, EncoderStatus.NotListed);
        }

        var options = strategy.GetQualityOptions(codec, QualityPreset.Balanced);
        var eightBit = await TestAsync(ffmpegPath, encoder, strategy.GetPixelFormat(codec, highBitDepth: false)!, options, cancellationToken).ConfigureAwait(false);
        if (eightBit is not null)
        {
            return new EncoderProbe(strategy.Vendor, codec, encoder, EncoderStatus.Unavailable, Failure: eightBit);
        }

        var highFormat = strategy.GetPixelFormat(codec, highBitDepth: true);
        var high = highFormat is not null
                   && await TestAsync(ffmpegPath, encoder, highFormat, options, cancellationToken).ConfigureAwait(false) is null;

        return new EncoderProbe(strategy.Vendor, codec, encoder, EncoderStatus.Available, SupportsHighBitDepth: high);
    }

    public static IReadOnlyList<string> Arguments(string encoder, string pixelFormat, IEnumerable<KeyValuePair<string, string>> options)
    {
        List<string> arguments =
        [
            "-hide_banner", "-nostdin", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=30",
            "-frames:v", "5", "-pix_fmt", pixelFormat,
            "-c:v", encoder,
        ];

        foreach (var (name, value) in options)
        {
            arguments.AddRange(["-" + name, value]);
        }

        arguments.AddRange(["-f", "null", "-"]);
        return arguments;
    }

    /// <returns><see langword="null"/> on success, otherwise the classified failure.</returns>
    private static async Task<JobFailure?> TestAsync(
        string ffmpegPath, string encoder, string pixelFormat, IReadOnlyList<KeyValuePair<string, string>> options, CancellationToken cancellationToken)
    {
        ProcessOutput output;
        try
        {
            output = await ProcessCapture.RunAsync(ffmpegPath, Arguments(encoder, pixelFormat, options), Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            return new JobFailure(FailureCategory.DependencyMissing, $"FFmpeg at '{ffmpegPath}' could not be started.") { Detail = ex.Message };
        }

        var stderr = output.StandardError.Trim();
        if (output.Succeeded && stderr.Length == 0)
        {
            return null;
        }

        if (output.TimedOut)
        {
            return new JobFailure(FailureCategory.HardwareUnavailable, $"{encoder} did not respond within {Timeout.TotalSeconds:0} s.");
        }

        // Any failure of a generated test pattern means the device cannot do this encode, whatever FFmpeg called it.
        var classified = FFmpegFailureClassifier.Classify(output.ExitCode, stderr);
        return new JobFailure(FailureCategory.HardwareUnavailable, classified.Message) { Detail = classified.Detail };
    }
}
