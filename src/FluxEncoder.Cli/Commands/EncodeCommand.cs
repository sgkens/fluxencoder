using System.CommandLine;
using System.CommandLine.Parsing;
using FluxEncoder.Cli.Batching;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Parsing;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Cli.Commands;

/// <summary>
/// <c>encode</c>: convert one or more inputs. Every option maps to a policy defined in Core; defaults are the safe,
/// widely playable choices (H.264, balanced quality, all audio and subtitles, never overwrite).
/// </summary>
internal sealed class EncodeCommand
{
    public const string Name = "encode";

    /// <summary>
    /// Set to 1 by administrators of shared or service installs: disables <c>--ffmpeg-arg</c> regardless of settings.
    /// </summary>
    public const string ManagedVariable = "FLUXENCODER_MANAGED";

    private static readonly string[] Codecs = ["h264", "hevc", "av1", "vvc", "copy"];
    private static readonly string[] AudioCodecs = ["copy", "aac", "opus", "ac3", "eac3", "flac", "mp3"];
    private static readonly string[] Qualities = ["fast", "balanced", "high"];
    private static readonly string[] Hardware = ["auto", "cpu", "nvidia", "amd", "intel", "apple"];
    private static readonly string[] VideoSelections = ["primary", "all", "none"];

    public readonly Argument<string[]> Inputs = new("inputs")
    {
        Description = "Files, directories (their media files), or URLs to encode.",
        Arity = ArgumentArity.OneOrMore,
    };

    public readonly Option<string?> Output = new("--output", "-o")
    {
        Description = "Output file, or a directory for the results. Default: beside each input as NAME.CODEC.EXT.",
    };

    public readonly Option<string?> Format = new("--format", "-f")
    {
        Description = "Container when FluxEncoder names the output: mkv, mp4, webm, mov, ts, mka, m4a, mp3, flac, opus. Default: the input's, else mkv.",
    };

    public readonly Option<string> Codec = new("--codec", "-c")
    {
        Description = "Video codec, or copy to keep the video untouched.",
        DefaultValueFactory = _ => "h264",
    };

    public readonly Option<string> Quality = new("--quality", "-q")
    {
        Description = "Speed/quality trade-off.",
        DefaultValueFactory = _ => "balanced",
    };

    public readonly Option<int?> VideoBitRate = new("--video-bitrate")
    {
        Description = "Target video bit rate instead of constant quality, in kbit/s (4000) or Mbit/s (4M).",
        CustomParser = ParseBitRate,
    };

    public readonly Option<bool> TwoPass = new("--two-pass")
    {
        Description = "Analyse the input first to spend --video-bitrate better (CPU encoders only).",
    };

    public readonly Option<string> AudioCodec = new("--audio-codec", "-a")
    {
        Description = "Audio codec, or copy.",
        DefaultValueFactory = _ => "copy",
    };

    public readonly Option<int?> AudioBitRate = new("--audio-bitrate")
    {
        Description = "Audio bit rate in kbit/s. Default scales with channel count.",
        CustomParser = ParseBitRate,
    };

    public readonly Option<string> Video = new("--video")
    {
        Description = "Video streams to keep.",
        DefaultValueFactory = _ => "primary",
    };

    public readonly Option<string> Audio = new("--audio")
    {
        Description = "Audio tracks: all, default, none, or languages in preference order (jpn,eng).",
        DefaultValueFactory = _ => "all",
    };

    public readonly Option<string> Subtitles = new("--subs")
    {
        Description = "Subtitle tracks: all, default, none, or languages (eng,fre).",
        DefaultValueFactory = _ => "all",
    };

    public readonly Option<bool> NoChapters = new("--no-chapters") { Description = "Drop chapters." };

    public readonly Option<bool> NoMetadata = new("--no-metadata") { Description = "Drop container tags such as title (stream languages are kept)." };

    public readonly Option<bool> KeepPrivateMetadata = new("--keep-private-metadata")
    {
        Description = "Keep location (GPS) and recording-device tags, which are removed by default.",
    };

    public readonly Option<bool> NoCover = new("--no-cover") { Description = "Drop cover art." };

    public readonly Option<bool> NoAttachments = new("--no-attachments") { Description = "Drop attachments such as subtitle fonts." };

    public readonly Option<bool> KeepData = new("--keep-data") { Description = "Keep data streams such as timecode." };

    public readonly Option<TimeSpan?> Start = new("--start") { Description = "Start position in the input (90, 1:30, 1h2m).", CustomParser = ParseTime };

    public readonly Option<TimeSpan?> End = new("--end") { Description = "End position in the input. Cannot be combined with --duration.", CustomParser = ParseTime };

    public readonly Option<TimeSpan?> Duration = new("--duration") { Description = "Length to encode; for live input, a recording limit.", CustomParser = ParseTime };

    public readonly Option<string> HardwareOption = new("--hardware")
    {
        Description = "Encoder hardware. auto uses a working GPU when it suits the job, else the CPU.",
        DefaultValueFactory = _ => "auto",
    };

    public readonly Option<bool> HardwareFallback = new("--hardware-fallback")
    {
        Description = "With --hardware <vendor>, encode on the CPU if that GPU cannot.",
    };

    public readonly Option<bool> Overwrite = new("--overwrite") { Description = "Replace an existing output (only after the new one is complete)." };

    public readonly Option<bool> SkipExisting = new("--skip-existing") { Description = "Skip inputs whose output already exists." };

    public readonly Option<bool> RenameExisting = new("--rename-existing") { Description = "Write NAME (1).EXT when the output exists." };

    public readonly Option<bool> KeepPartial = new("--keep-partial") { Description = "Keep the incomplete output of a failed or cancelled encode." };

    public readonly Option<int> Retries = new("--retries")
    {
        Description = "Retry network and stall failures this many times.",
        DefaultValueFactory = _ => 0,
    };

    public readonly Option<bool> FailFast = new("--fail-fast") { Description = "Stop the batch at the first failure." };

    public readonly Option<bool> DryRun = new("--dry-run") { Description = "Probe and plan, show the FFmpeg commands (secrets masked), and stop." };

    public readonly Option<bool> Json = new("--json") { Description = "Write one JSON document to stdout and nothing else." };

    public readonly Option<bool> Quiet = new("--quiet") { Description = "Print errors only." };

    public readonly Option<bool> Verbose = new("--verbose") { Description = "Also print FFmpeg commands and full error output." };

    public readonly Option<bool> NoColor = new("--no-color") { Description = "Disable colour (also NO_COLOR=1)." };

    public readonly Option<string[]> FFmpegArgs = new("--ffmpeg-arg")
    {
        Description = "Pass one raw argument to FFmpeg among the output options; repeat for more (--ffmpeg-arg -tune --ffmpeg-arg film). " +
            "Raw arguments bypass FluxEncoder's checks and can read or write any file or URL. Disabled when FLUXENCODER_MANAGED=1.",
        AllowMultipleArgumentsPerToken = false,
        Arity = ArgumentArity.ZeroOrMore,
    };

    public readonly Option<string?> Preset = new("--preset", "-p")
    {
        Description = "Use a named preset from settings.json; typed options still win. See 'config init'.",
    };

    public readonly Option<bool> NoConfig = new("--no-config")
    {
        Description = "Ignore the encode defaults in settings.json (presets still work with --preset).",
    };

    public readonly Option<string?> FFmpeg = new("--ffmpeg") { Description = "FFmpeg executable or its directory." };

    public readonly Option<string?> FFprobe = new("--ffprobe") { Description = "FFprobe executable or its directory." };

    /// <summary>
    /// A fresh command with its own option instances, so building the tree twice never shares validators.
    /// </summary>
    public static Command Build(CliHost host, CliSession session)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(session);
        return new EncodeCommand().Create(host, session);
    }

    /// <summary>
    /// The command together with its option definitions, for <c>apply</c>, which parses each Fluxfile job through it.
    /// </summary>
    internal static (EncodeCommand Definition, Command Command) Define(CliHost host, CliSession session)
    {
        var definition = new EncodeCommand();
        return (definition, definition.Create(host, session));
    }

    private Command Create(CliHost host, CliSession session)
    {
        Codec.AcceptOnlyFromAmong(Codecs);
        Quality.AcceptOnlyFromAmong(Qualities);
        AudioCodec.AcceptOnlyFromAmong(AudioCodecs);
        Video.AcceptOnlyFromAmong(VideoSelections);
        HardwareOption.AcceptOnlyFromAmong(Hardware);
        Audio.Validators.Add(ValidateTracks);
        Subtitles.Validators.Add(ValidateTracks);
        Retries.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() is < 0 or > 10)
            {
                result.AddError("--retries must be between 0 and 10.");
            }
        });

        var command = new Command(Name, "Convert media files with FFmpeg: re-encode or copy video, choose tracks, trim, and batch.")
        {
            Inputs, Output, Format, Codec, Quality, VideoBitRate, TwoPass, AudioCodec, AudioBitRate,
            Video, Audio, Subtitles, NoChapters, NoMetadata, KeepPrivateMetadata, NoCover, NoAttachments, KeepData,
            Start, End, Duration, HardwareOption, HardwareFallback,
            Overwrite, SkipExisting, RenameExisting, KeepPartial, Retries, FailFast,
            DryRun, Json, Quiet, Verbose, NoColor, Preset, NoConfig, FFmpegArgs, FFmpeg, FFprobe,
        };

        command.Validators.Add(Validate);
        command.SetAction((result, token) => RunAsync(result, host, session, token));
        return command;
    }

    private void Validate(CommandResult result)
    {
        // Options with their own errors throw when read; those errors are already reported, so treat them as unset.
        // Options left at their default read as null here, so defaults are spelled out at each use.
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

        if (Get(End) is not null && Get(Duration) is not null)
        {
            result.AddError("--end and --duration both set the length; give one.");
        }

        if (Get(Start) is { } start && Get(End) is { } end && end <= start)
        {
            result.AddError("--end must be after --start.");
        }

        if (Get(Duration) is { } duration && duration <= TimeSpan.Zero)
        {
            result.AddError("--duration must be greater than zero.");
        }

        var copy = Get(Codec) == "copy";
        if (copy && (Get(VideoBitRate) is not null || Get(TwoPass)))
        {
            result.AddError("--video-bitrate and --two-pass need a video codec; they do nothing with --codec copy.");
        }

        if (Get(TwoPass) && Get(VideoBitRate) is null)
        {
            result.AddError("--two-pass needs --video-bitrate.");
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
        var settings = OutputSettings.Resolve(host, parse.GetValue(Json), parse.GetValue(Quiet), parse.GetValue(Verbose), parse.GetValue(NoColor));
        IReporter reporter = settings.Json ? new JsonReporter(host) : new HumanReporter(host, settings);
        var dryRun = parse.GetValue(DryRun);

        foreach (var notice in session.Notices)
        {
            reporter.Notice(notice);
        }

        var (jobs, usage) = CreateJobs(parse);
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

        var reports = new List<JobReport>();
        var notStarted = 0;
        var failFast = parse.GetValue(FailFast);

        for (var i = 0; i < jobs.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                notStarted = jobs.Count - i;
                break;
            }

            var (input, job) = jobs[i];
            var report = await run.Value.RunAsync(i, jobs.Count, input, input, job, cancellationToken).ConfigureAwait(false);
            reports.Add(report);

            var failure = report.Result.Failure;
            if (failure is { Category: FailureCategory.Cancelled } || (failure is not null && failFast))
            {
                notStarted = jobs.Count - i - 1;
                break;
            }
        }

        var batch = new BatchResult(reports.Select(r => r.Result)) { NotStarted = notStarted };
        reporter.Finished(reports, batch, dryRun);
        return (int)batch.ExitCode;
    }

    /// <summary>
    /// Expands the inputs (directories become their media files) and builds one job per input from the parsed options.
    /// Shared with <c>apply</c>, which parses each Fluxfile job through this command so both accept exactly the same
    /// options with the same validation.
    /// </summary>
    internal (IReadOnlyList<(string Input, EncodingJob Job)> Jobs, JobFailure? Failure) CreateJobs(ParseResult parse)
    {
        ArgumentNullException.ThrowIfNull(parse);

        var (inputs, emptyDirectories) = InputExpander.Expand(parse.GetValue(Inputs) ?? []);
        if (emptyDirectories.Count > 0 && inputs.Count == 0)
        {
            return ([], new JobFailure(FailureCategory.InvalidUsage, $"No media files found in {string.Join(", ", emptyDirectories.Select(d => $"'{d}'"))}."));
        }

        var output = parse.GetValue(Output);
        var several = inputs.Count > 1;
        if (several && output is not null && !OutputNamer.IsDirectoryLike(output) && ContainerProfile.ForPath(output) is not null)
        {
            return ([], new JobFailure(FailureCategory.InvalidUsage, $"With {inputs.Count} inputs, --output must be a directory, not the file '{output}'."));
        }

        var jobs = new List<(string, EncodingJob)>(inputs.Count);
        foreach (var input in inputs)
        {
            var video = VideoTarget(parse);
            jobs.Add((input, BuildJob(parse, input, OutputNamer.Resolve(input, output, parse.GetValue(Format), video, several), video)));
        }

        return (jobs, null);
    }

    private VideoTarget VideoTarget(ParseResult parse)
    {
        var codec = parse.GetValue(Codec);
        if (codec == "copy")
        {
            return new VideoTarget.StreamCopy();
        }

        var quality = Enum.Parse<QualityPreset>(parse.GetValue(Quality)!, ignoreCase: true);
        return new VideoTarget.Encode(Enum.Parse<VideoCodec>(codec!, ignoreCase: true), quality)
        {
            TargetBitRateKbps = parse.GetValue(VideoBitRate),
            TwoPass = parse.GetValue(TwoPass),
        };
    }

    private EncodingJob BuildJob(ParseResult parse, string input, string output, VideoTarget video)
    {
        var audioCodec = parse.GetValue(AudioCodec);
        AudioTarget audio = audioCodec == "copy"
            ? new AudioTarget.StreamCopy()
            : new AudioTarget.Encode(Enum.Parse<Core.Encoding.AudioCodec>(audioCodec!, ignoreCase: true), parse.GetValue(AudioBitRate));

        var start = parse.GetValue(Start);
        var end = parse.GetValue(End);
        var duration = parse.GetValue(Duration);

        var hardware = parse.GetValue(HardwareOption) switch
        {
            "cpu" => HardwareVendor.Cpu,
            "nvidia" => HardwareVendor.Nvidia,
            "amd" => HardwareVendor.Amd,
            "intel" => HardwareVendor.Intel,
            "apple" => HardwareVendor.Apple,
            _ => (HardwareVendor?)null,
        };

        return new EncodingJob
        {
            InputPath = input,
            OutputPath = output,
            Video = video,
            Audio = audio,
            Hardware = hardware,
            HardwareFallback = parse.GetValue(HardwareFallback),
            ExtraArguments = [.. parse.GetValue(FFmpegArgs) ?? []],
            Trim = start is null && end is null && duration is null ? null : new TrimRange(start, end, duration),
            Streams = new StreamSelection
            {
                Video = Enum.Parse<VideoSelection>(parse.GetValue(Video)!, ignoreCase: true),
                Audio = Tracks(parse.GetValue(Audio)),
                Subtitles = Tracks(parse.GetValue(Subtitles)),
                Chapters = !parse.GetValue(NoChapters),
                Metadata = !parse.GetValue(NoMetadata),
                PrivateMetadata = parse.GetValue(KeepPrivateMetadata),
                CoverArt = !parse.GetValue(NoCover),
                Attachments = !parse.GetValue(NoAttachments),
                DataStreams = parse.GetValue(KeepData),
            },
            Policy = new JobPolicy
            {
                Overwrite = parse.GetValue(Overwrite) ? OverwritePolicy.Overwrite
                    : parse.GetValue(SkipExisting) ? OverwritePolicy.Skip
                    : parse.GetValue(RenameExisting) ? OverwritePolicy.Rename
                    : OverwritePolicy.Never,
                PartialOutput = parse.GetValue(KeepPartial) ? PartialOutputPolicy.Keep : PartialOutputPolicy.Delete,
                Retry = parse.GetValue(Retries) is > 0 and var retries ? new RetryPolicy(retries + 1, TimeSpan.FromSeconds(2)) : RetryPolicy.None,
            },
        };
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

    private static TimeSpan? ParseTime(ArgumentResult result)
    {
        if (ValueParsers.TryParseTime(result.Tokens.Single().Value, out var time, out var error))
        {
            return time;
        }

        result.AddError(error!);
        return null;
    }

    private static void ValidateTracks(OptionResult result)
    {
        if (!ValueParsers.TryParseTracks(result.GetValueOrDefault<string>(), out _, out var error))
        {
            result.AddError(error!);
        }
    }

    private static TrackSelection Tracks(string? value) =>
        ValueParsers.TryParseTracks(value, out var selection, out _) ? selection : new TrackSelection.All();
}
