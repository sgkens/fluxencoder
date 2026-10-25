using System.CommandLine;
using System.CommandLine.Parsing;
using FluxEncoder.Cli.Batching;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Parsing;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Configuration;

namespace FluxEncoder.Cli.Commands;

/// <summary>
/// <c>merge</c>: join two or more media files end to end into a single output (TSKE012.1).
/// </summary>
/// <remarks>
/// <c>--mode auto</c> copies streams through an <c>ffconcat</c> list when every input matches and the target container
/// accepts the codecs, and re-encodes through a normalizing <c>concat</c> filter graph otherwise.
/// </remarks>
internal sealed class MergeCommand
{
    public const string Name = "merge";

    private static readonly string[] Modes = ["auto", "copy", "reencode"];
    private static readonly string[] Codecs = ["copy", "h264", "hevc", "av1", "vvc"];
    private static readonly string[] AudioCodecs = ["copy", "aac", "opus", "ac3", "eac3", "flac", "mp3"];
    private static readonly string[] Qualities = ["fast", "balanced", "high"];
    private static readonly string[] Hardware = ["auto", "cpu", "nvidia", "amd", "intel", "apple"];

    public readonly Argument<string[]> Inputs = new("inputs")
    {
        Description = "Two or more media files (or directories of media files) to join in order.",
        Arity = ArgumentArity.OneOrMore,
    };

    public readonly Option<string> Output = new("--output", "-o")
    {
        Description = "Output file for the merged video.",
        Required = true,
    };

    public readonly Option<string> Mode = new("--mode")
    {
        Description = "auto copies when inputs match, else re-encodes; copy fails if they differ; reencode always normalizes.",
        DefaultValueFactory = _ => "auto",
    };

    public readonly Option<string> Codec = new("--codec", "-c")
    {
        Description = "Video codec when re-encoding, or copy to stream-copy when compatible.",
        DefaultValueFactory = _ => "copy",
    };

    public readonly Option<string> Quality = new("--quality", "-q")
    {
        Description = "Speed/quality trade-off when re-encoding.",
        DefaultValueFactory = _ => "balanced",
    };

    public readonly Option<int?> VideoBitRate = new("--video-bitrate")
    {
        Description = "Target video bit rate when re-encoding, in kbit/s (4000) or Mbit/s (4M).",
        CustomParser = ParseBitRate,
    };

    public readonly Option<string> AudioCodec = new("--audio-codec", "-a")
    {
        Description = "Audio codec when re-encoding, or copy.",
        DefaultValueFactory = _ => "copy",
    };

    public readonly Option<int?> AudioBitRate = new("--audio-bitrate")
    {
        Description = "Audio bit rate in kbit/s when re-encoding.",
        CustomParser = ParseBitRate,
    };

    public readonly Option<string> HardwareOption = new("--hardware")
    {
        Description = "Encoder hardware when re-encoding: auto, cpu, nvidia, amd, intel, or apple.",
        DefaultValueFactory = _ => "auto",
    };

    public readonly Option<bool> HardwareFallback = new("--hardware-fallback")
    {
        Description = "With --hardware <vendor>, encode on the CPU if that GPU cannot.",
    };

    public readonly Option<bool> NoChapters = new("--no-chapters") { Description = "Drop chapters." };

    public readonly Option<bool> NoMetadata = new("--no-metadata") { Description = "Drop container tags." };

    public readonly Option<bool> KeepPrivateMetadata = new("--keep-private-metadata")
    {
        Description = "Keep location (GPS) and recording-device tags, which are removed by default.",
    };

    public readonly Option<bool> Overwrite = new("--overwrite") { Description = "Replace an existing output (only after the new one is complete)." };

    public readonly Option<bool> SkipExisting = new("--skip-existing") { Description = "Skip when the output already exists." };

    public readonly Option<bool> RenameExisting = new("--rename-existing") { Description = "Write NAME (1).EXT when the output exists." };

    public readonly Option<bool> KeepPartial = new("--keep-partial") { Description = "Keep the incomplete output of a failed or cancelled merge." };

    public readonly Option<bool> DryRun = new("--dry-run") { Description = "Probe and plan, show the FFmpeg commands, and stop." };

    public readonly Option<bool> Json = new("--json") { Description = "Write one JSON document to stdout and nothing else." };

    public readonly Option<bool> Quiet = new("--quiet") { Description = "Print errors only." };

    public readonly Option<bool> Verbose = new("--verbose") { Description = "Also print FFmpeg commands and full error output." };

    public readonly Option<bool> NoColor = new("--no-color") { Description = "Disable colour (also NO_COLOR=1)." };

    public readonly Option<string?> FFmpeg = new("--ffmpeg") { Description = "FFmpeg executable or its directory." };

    public readonly Option<string?> FFprobe = new("--ffprobe") { Description = "FFprobe executable or its directory." };

    public static Command Build(CliHost host, CliSession session)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(session);
        return new MergeCommand().Create(host, session);
    }

    private Command Create(CliHost host, CliSession session)
    {
        Mode.AcceptOnlyFromAmong(Modes);
        Codec.AcceptOnlyFromAmong(Codecs);
        Quality.AcceptOnlyFromAmong(Qualities);
        AudioCodec.AcceptOnlyFromAmong(AudioCodecs);
        HardwareOption.AcceptOnlyFromAmong(Hardware);

        var command = new Command(Name, "Join two or more media files end to end into a single output.")
        {
            Inputs, Output, Mode, Codec, Quality, VideoBitRate, AudioCodec, AudioBitRate,
            HardwareOption, HardwareFallback, NoChapters, NoMetadata, KeepPrivateMetadata,
            Overwrite, SkipExisting, RenameExisting, KeepPartial,
            DryRun, Json, Quiet, Verbose, NoColor, FFmpeg, FFprobe,
        };

        command.Validators.Add(Validate);
        command.SetAction((result, token) => RunAsync(result, host, session, token));
        return command;
    }

    private void Validate(CommandResult result)
    {
        T? Get<T>(Option<T> option)
        {
            try
            {
                return result.GetValue(option);
            }
            catch (InvalidOperationException)
            {
                return default;
            }
        }

        var overwriteFlags = new[] { Overwrite, SkipExisting, RenameExisting }.Count(o => Get(o));
        if (overwriteFlags > 1)
        {
            result.AddError("Choose one of --overwrite, --skip-existing, or --rename-existing.");
        }

        var mode = Get(Mode) ?? "auto";
        var codec = Get(Codec) ?? "copy";
        if (mode == "copy" && codec != "copy")
        {
            result.AddError("--mode copy cannot be combined with a re-encode --codec.");
        }

        if (Get(VideoBitRate) is not null && codec == "copy" && mode == "copy")
        {
            result.AddError("--video-bitrate cannot be used with --mode copy.");
        }

        if (Get(AudioBitRate) is not null && (Get(AudioCodec) ?? "copy") == "copy")
        {
            result.AddError("--audio-bitrate needs --audio-codec; copied audio keeps its bit rate.");
        }

        if (Get(HardwareFallback) && (Get(HardwareOption) ?? "auto") is "auto" or "cpu")
        {
            result.AddError("--hardware-fallback only applies with --hardware nvidia, amd, intel, or apple.");
        }

        if (Get(Json) && (Get(Quiet) || Get(Verbose)))
        {
            result.AddError("--json already prints only the JSON document; it cannot be combined with --quiet or --verbose.");
        }

        if (Get(Quiet) && Get(Verbose))
        {
            result.AddError("Choose either --quiet or --verbose.");
        }
    }

    private async Task<int> RunAsync(ParseResult parse, CliHost host, CliSession session, CancellationToken cancellationToken)
    {
        var (loadedSettings, warnings) = SettingsStore.Load(host.Paths.SettingsFile);
        session.Settings = loadedSettings;
        session.Notices.AddRange(warnings);

        var outputSettings = OutputSettings.Resolve(host, parse.GetValue(Json), parse.GetValue(Quiet), parse.GetValue(Verbose), parse.GetValue(NoColor));
        IReporter reporter = outputSettings.Json ? new JsonReporter(host) : new HumanReporter(host, outputSettings);
        var dryRun = parse.GetValue(DryRun);

        foreach (var notice in session.Notices)
        {
            reporter.Notice(notice);
        }

        var (job, usage) = CreateJob(parse);
        if (usage is not null)
        {
            reporter.Fatal(usage);
            return (int)usage.ExitCode;
        }

        var run = await EncodeRun.StartAsync(host, session, reporter, parse.GetValue(FFmpeg), parse.GetValue(FFprobe), dryRun, cancellationToken).ConfigureAwait(false);
        if (!run.IsSuccess)
        {
            reporter.Fatal(run.Failure);
            return (int)run.Failure.ExitCode;
        }

        var label = string.Join(" + ", job!.Inputs.Select(Path.GetFileName));
        var report = await run.Value.RunConcatAsync(label, job, cancellationToken).ConfigureAwait(false);
        var batch = new BatchResult([report.Result]);
        reporter.Finished([report], batch, dryRun);
        return (int)batch.ExitCode;
    }

    internal (ConcatJob? Job, JobFailure? Failure) CreateJob(ParseResult parse)
    {
        ArgumentNullException.ThrowIfNull(parse);

        var (inputs, emptyDirectories) = InputExpander.Expand(parse.GetValue(Inputs) ?? []);
        if (emptyDirectories.Count > 0 && inputs.Count == 0)
        {
            return (null, new JobFailure(FailureCategory.InvalidUsage, $"No media files found in {string.Join(", ", emptyDirectories.Select(d => $"'{d}'"))}."));
        }

        if (inputs.Count < 2)
        {
            return (null, new JobFailure(FailureCategory.InvalidUsage, "merge needs at least two input files."));
        }

        var mode = Enum.Parse<ConcatMode>(parse.GetValue(Mode)!, ignoreCase: true);
        var codecName = parse.GetValue(Codec)!;
        var quality = Enum.Parse<QualityPreset>(parse.GetValue(Quality)!, ignoreCase: true);
        var bitRate = parse.GetValue(VideoBitRate);

        VideoTarget video = codecName == "copy" && bitRate is null
            ? new VideoTarget.StreamCopy()
            : new VideoTarget.Encode(codecName == "copy" ? VideoCodec.H264 : Enum.Parse<VideoCodec>(codecName, ignoreCase: true), quality)
            {
                TargetBitRateKbps = bitRate,
            };

        var audioCodec = parse.GetValue(AudioCodec)!;
        AudioTarget audio = audioCodec == "copy"
            ? new AudioTarget.StreamCopy()
            : new AudioTarget.Encode(Enum.Parse<Core.Encoding.AudioCodec>(audioCodec, ignoreCase: true), parse.GetValue(AudioBitRate));

        var hardware = parse.GetValue(HardwareOption) switch
        {
            "cpu" => HardwareVendor.Cpu,
            "nvidia" => HardwareVendor.Nvidia,
            "amd" => HardwareVendor.Amd,
            "intel" => HardwareVendor.Intel,
            "apple" => HardwareVendor.Apple,
            _ => (HardwareVendor?)null,
        };

        var job = new ConcatJob(inputs, parse.GetValue(Output)!, video, audio)
        {
            Mode = mode,
            Hardware = hardware,
            HardwareFallback = parse.GetValue(HardwareFallback),
            KeepMetadata = !parse.GetValue(NoMetadata),
            KeepPrivateMetadata = parse.GetValue(KeepPrivateMetadata),
            KeepChapters = !parse.GetValue(NoChapters),
            Policy = new JobPolicy
            {
                Overwrite = parse.GetValue(Overwrite) ? OverwritePolicy.Overwrite
                    : parse.GetValue(SkipExisting) ? OverwritePolicy.Skip
                    : parse.GetValue(RenameExisting) ? OverwritePolicy.Rename
                    : OverwritePolicy.Never,
                PartialOutput = parse.GetValue(KeepPartial) ? PartialOutputPolicy.Keep : PartialOutputPolicy.Delete,
            },
        };

        return (job, null);
    }

    private static int? ParseBitRate(ArgumentResult result)
    {
        if (ValueParsers.TryParseBitRate(result.Tokens.Single().Value, out var kbps, out var error))
        {
            return kbps;
        }

        result.AddError(error!);
        return null;
    }
}
