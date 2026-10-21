using System.Globalization;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// Turns an <see cref="EncodingJob"/> and its probed input into an executable <see cref="OutputPlan"/> without
/// running anything. The same job, input, and context always produce the same arguments, so plans are
/// verified with snapshot tests and shown by <c>--dry-run</c>.
/// </summary>
/// <remarks>
/// <para>
/// Argument layout:
/// <c>[-ss start] [network reconnect] -i input [-t length] (-map 0:N)… per-stream codecs, options, and
/// dispositions [-map_metadata:g -1] [-map_chapters -1] [-movflags +faststart] [-pass 2 -passlogfile log]
/// -n -f muxer temporary-output</c>.
/// </para>
/// <para>
/// Trim semantics (TSKE005.5): <c>--start</c> and <c>--end</c> are absolute input positions; <c>--duration</c> is a
/// length from the start. Seeking is an input option (<c>-ss</c> before <c>-i</c>) for speed, plus <c>-ss 0</c> after
/// <c>-i</c> so copied audio and subtitles are cut exactly too; a copied video stream instead starts at the keyframe
/// before the cut. The encoded length is passed as <c>-t</c> because input seeking resets output timestamps to
/// zero, which would make <c>-to</c> relative anyway.
/// </para>
/// <para>
/// Two-pass (TSKE005.6): pass 1 maps only the transcoded video, disables audio, subtitles, and data, and writes
/// with <c>-f null</c> to the platform null device; pass 2 is the full encode. Both share a pass-log prefix in a
/// per-job scratch directory that the executor deletes.
/// </para>
/// </remarks>
public static class EncodePlanner
{
    public static Result<PlannedJob> Plan(EncodingJob job, PlanningInput input, PlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        var warnings = new List<JobWarning>();

        var profile = ContainerProfile.ForPath(job.OutputPath);
        if (profile is null)
        {
            var extension = Path.GetExtension(job.OutputPath);
            return Fail(FailureCategory.InvalidUsage, extension.Length == 0
                ? $"Output '{job.OutputPath}' has no file extension, so the format is unknown. Supported: {SupportedList()}."
                : $"Output format '{extension}' is not supported. Supported: {SupportedList()}.");
        }

        if (!context.Capabilities.Muxers.Contains(profile.Muxer))
        {
            return Fail(FailureCategory.Unsupported, $"This FFmpeg build cannot write {profile.Muxer} files.");
        }

        var (timeline, seek, length, trimFailure) = ResolveTrim(job, input, warnings);
        if (trimFailure is not null)
        {
            return trimFailure;
        }

        IReadOnlyList<FittedStream> streams = [];
        var keepChapters = true;
        var keepMetadata = true;

        if (input.IsProbed)
        {
            var fit = StreamFitter.Apply(job, input.Media, StreamMapper.Map(job, input.Media), profile, context);
            warnings.AddRange(fit.Warnings);
            if (fit.Failure is not null)
            {
                return fit.Failure;
            }

            streams = fit.Streams;
            keepChapters = fit.KeepChapters;
            keepMetadata = fit.KeepMetadata;
        }
        else if (job.Streams != StreamSelection.Default)
        {
            return Fail(FailureCategory.InvalidUsage, "Stream selection needs a probed input; pipe input uses FFmpeg's default stream choice.");
        }

        if (seek > TimeSpan.Zero && streams.Any(s => s.IsMotionVideo && s.IsCopy))
        {
            warnings.Add(new JobWarning(WarningCodes.TrimKeyframeAligned, "Video is copied, so the start moves back to the nearest keyframe. Re-encode for an exact cut."));
        }

        var encode = job.Video as VideoTarget.Encode;
        var twoPass = encode is { TwoPass: true };
        if (twoPass)
        {
            if (encode!.TargetBitRateKbps is null)
            {
                return Fail(FailureCategory.InvalidUsage, "Two-pass encoding needs a target bit rate (--video-bitrate).");
            }

            if (!timeline.SupportsMultiPass)
            {
                return Fail(FailureCategory.InvalidUsage, "Two-pass encoding needs a finite input; live and pipe inputs can be read only once.");
            }

            if (!context.Hardware.SupportsTwoPass(encode.Codec))
            {
                return Fail(FailureCategory.Unsupported, $"Two-pass is not supported for {encode.Codec}; use a quality preset instead, which needs only one pass.");
            }

            if (!streams.Any(s => s.IsMotionVideo && !s.IsCopy) && input.IsProbed)
            {
                return Fail(FailureCategory.InvalidUsage, "Two-pass needs a video stream to encode.");
            }
        }

        var copied = streams.Count == 0 ? 1.0 : streams.Count(s => s.IsCopy) / (double)streams.Count;
        var trimmedFraction = timeline is InputTimeline.Finite finite && input.Media.Duration is { } full && full > TimeSpan.Zero
            ? Math.Min(1.0, finite.Duration / full)
            : 1.0;

        var path = OutputPathPlanner.Resolve(job, input, context, profile, Math.Max(copied, 0.5) * trimmedFraction);
        warnings.AddRange(path.Warnings);
        if (path.Failure is not null)
        {
            return path.Failure;
        }

        if (path.SkipReason is not null)
        {
            return Result<PlannedJob>.Success(PlannedJob.Skip(path.FinalPath!, path.SkipReason, timeline, warnings));
        }

        var token = context.NewToken();
        var final = path.FinalPath!;
        var temporary = OutputPaths.Temporary(final, token);
        var scratch = twoPass ? Path.Combine(context.ScratchRoot, "flux-" + token) : null;
        var passLog = scratch is null ? null : Path.Combine(scratch, "pass");

        var transcodesVideo = input.IsProbed ? streams.Any(s => s.IsMotionVideo && !s.IsCopy) : job.Video is VideoTarget.Encode;
        var head = InputArguments(input, seek, length, exactCut: !streams.Any(s => s.IsMotionVideo && s.IsCopy), transcodesVideo ? context.Hardware.DecodeArguments : []);
        var invocations = new List<FFmpegInvocation>();

        if (twoPass)
        {
            invocations.Add(new FFmpegInvocation(
            [
                .. head,
                .. StreamArguments(streams.Where(s => s.IsMotionVideo && !s.IsCopy), firstPassOnly: true),
                "-pass", "1", "-passlogfile", passLog!,
                "-an", "-sn", "-dn",
                "-f", "null", context.NullDevice,
            ]));
        }

        var main = new List<string>(head);
        if (input.IsProbed)
        {
            main.AddRange(StreamArguments(streams, firstPassOnly: false));
        }
        else if (UnprobedCodecArguments(job, context) is { } codecs)
        {
            main.AddRange(codecs);
        }
        else
        {
            return Fail(FailureCategory.Unsupported, "This FFmpeg build has no encoder for the requested codecs.");
        }

        if (!keepMetadata)
        {
            main.AddRange(["-map_metadata:g", "-1"]);
        }

        if (!keepChapters)
        {
            main.AddRange(["-map_chapters", "-1"]);
        }

        if (profile.FastStart)
        {
            main.AddRange(["-movflags", "+faststart"]);
        }

        if (twoPass)
        {
            main.AddRange(["-pass", "2", "-passlogfile", passLog!]);
        }

        main.AddRange(["-n", "-f", profile.Muxer, temporary]);
        invocations.Add(new FFmpegInvocation(main));

        var plan = new OutputPlan(job, invocations)
        {
            FinalOutputPath = final,
            TemporaryOutputPath = temporary,
            ScratchDirectory = scratch,
        };

        return Result<PlannedJob>.Success(PlannedJob.Run(plan, timeline, warnings));

        Result<PlannedJob> Fail(FailureCategory category, string message) => new JobFailure(category, message);
    }

    /// <summary>
    /// Formats a time as seconds with up to microsecond precision, the form FFmpeg's time options accept.
    /// </summary>
    public static string FormatSeconds(TimeSpan time) =>
        (time.Ticks / (decimal)TimeSpan.TicksPerSecond).ToString("0.######", CultureInfo.InvariantCulture);

    private static (InputTimeline Timeline, TimeSpan Seek, TimeSpan? Length, JobFailure? Failure) ResolveTrim(
        EncodingJob job, PlanningInput input, List<JobWarning> warnings)
    {
        var timeline = input.IsProbed ? InputTimeline.From(input.Media) : new InputTimeline.Unbounded();

        if (job.Trim is not { } trim)
        {
            return (timeline, TimeSpan.Zero, null, null);
        }

        if (timeline is not InputTimeline.Finite finite)
        {
            if (trim.Start is not null || trim.End is not null)
            {
                return (timeline, TimeSpan.Zero, null, new JobFailure(
                    FailureCategory.InvalidUsage,
                    "This input has no known length, so only --duration (a recording limit) applies, not --start or --end."));
            }

            return (timeline, TimeSpan.Zero, trim.Duration, null);
        }

        var start = trim.Start ?? TimeSpan.Zero;
        if (start >= finite.Duration)
        {
            return (timeline, TimeSpan.Zero, null, new JobFailure(
                FailureCategory.InvalidUsage,
                $"--start {FormatSeconds(start)}s is at or past the end of the input ({FormatSeconds(finite.Duration)}s)."));
        }

        var end = start + (trim.Length ?? finite.Duration - start);
        if (end > finite.Duration)
        {
            warnings.Add(new JobWarning(
                WarningCodes.TrimEndBeyondInput,
                $"The trim ends at {FormatSeconds(end)}s but the input ends at {FormatSeconds(finite.Duration)}s; encoding stops there."));
            end = finite.Duration;
        }

        var length = trim.Length is null ? (TimeSpan?)null : end - start;
        return (new InputTimeline.Finite(end - start), start, length, null);
    }

    /// <param name="exactCut">
    /// Add an output-side <c>-ss 0</c> after an input seek. Input seeking is fast but leaves stream-copied packets
    /// from before the seek point in the output with negative timestamps (FFmpeg 8 keeps 1 s of copied audio
    /// pre-roll, for example); the output-side seek drops them so every stream starts at the cut. Not used when the
    /// video itself is copied, where dropping those packets would remove the keyframe the first frames depend on.
    /// </param>
    private static List<string> InputArguments(PlanningInput input, TimeSpan seek, TimeSpan? length, bool exactCut, IReadOnlyList<string> decode)
    {
        var arguments = new List<string>(decode);

        if (seek > TimeSpan.Zero)
        {
            arguments.AddRange(["-ss", FormatSeconds(seek)]);
        }

        if (input.Source is { Kind: InputKind.Network, Scheme: "http" or "https" })
        {
            arguments.AddRange(["-reconnect", "1", "-reconnect_streamed", "1", "-reconnect_delay_max", "30"]);
        }

        arguments.AddRange(["-i", input.Source.Value]);

        if (seek > TimeSpan.Zero && exactCut)
        {
            arguments.AddRange(["-ss", "0"]);
        }

        if (length is { } t)
        {
            arguments.AddRange(["-t", FormatSeconds(t)]);
        }

        return arguments;
    }

    private static IEnumerable<string> StreamArguments(IEnumerable<FittedStream> streams, bool firstPassOnly)
    {
        var list = streams.ToList();

        foreach (var stream in list)
        {
            yield return "-map";
            yield return "0:" + stream.Source.Index.ToString(CultureInfo.InvariantCulture);
        }

        foreach (var stream in list)
        {
            yield return $"-c:{stream.Specifier}";
            yield return stream.Codec;

            foreach (var (name, value) in stream.Options)
            {
                // Stream metadata takes the "s:" metadata specifier; codec options take the stream specifier.
                yield return name == "metadata" ? $"-metadata:s:{stream.Specifier}" : $"-{name}:{stream.Specifier}";
                yield return value;
            }

            if (!firstPassOnly && stream.Type is 'v' or 'a' or 's')
            {
                yield return $"-disposition:{stream.Specifier}";
                yield return DispositionText(stream.Disposition);
            }
        }
    }

    /// <summary>
    /// Codec choice for an unprobed (pipe) input, where FFmpeg selects the streams itself, so options apply to
    /// every stream of a type rather than to mapped outputs.
    /// </summary>
    private static List<string>? UnprobedCodecArguments(EncodingJob job, PlanningContext context)
    {
        var arguments = new List<string>();

        if (job.Video is VideoTarget.Encode video)
        {
            if (!context.Hardware.TryGetVideoEncoder(video.Codec, out var encoder) || !context.Capabilities.HasEncoder(encoder))
            {
                return null;
            }

            arguments.AddRange(["-c:v", encoder]);
            var options = video.TargetBitRateKbps is { } kbps
                ? [.. context.Hardware.GetBitRateOptions(video.Codec, video.Quality), new("b", kbps.ToString(CultureInfo.InvariantCulture) + "k")]
                : context.Hardware.GetQualityOptions(video.Codec, video.Quality);
            foreach (var (name, value) in options)
            {
                arguments.AddRange([$"-{name}:v", value]);
            }
        }
        else
        {
            arguments.AddRange(["-c:v", "copy"]);
        }

        if (job.Audio is AudioTarget.Encode audio)
        {
            var encoder = StreamFitter.AudioEncoder(audio.Codec);
            if (!context.Capabilities.HasEncoder(encoder))
            {
                return null;
            }

            arguments.AddRange(["-c:a", encoder]);
        }
        else
        {
            arguments.AddRange(["-c:a", "copy"]);
        }

        return arguments;
    }

    private static readonly (StreamDisposition Flag, string Name)[] DispositionNames =
    [
        (StreamDisposition.Default, "default"),
        (StreamDisposition.Dub, "dub"),
        (StreamDisposition.Original, "original"),
        (StreamDisposition.Comment, "comment"),
        (StreamDisposition.Lyrics, "lyrics"),
        (StreamDisposition.Karaoke, "karaoke"),
        (StreamDisposition.Forced, "forced"),
        (StreamDisposition.HearingImpaired, "hearing_impaired"),
        (StreamDisposition.VisualImpaired, "visual_impaired"),
        (StreamDisposition.CleanEffects, "clean_effects"),
        (StreamDisposition.AttachedPicture, "attached_pic"),
        (StreamDisposition.TimedThumbnails, "timed_thumbnails"),
        (StreamDisposition.NonDiegetic, "non_diegetic"),
        (StreamDisposition.Captions, "captions"),
        (StreamDisposition.Descriptions, "descriptions"),
        (StreamDisposition.Metadata, "metadata"),
        (StreamDisposition.Dependent, "dependent"),
        (StreamDisposition.StillImage, "still_image"),
    ];

    /// <summary>
    /// FFmpeg's <c>-disposition</c> value: flags joined with <c>+</c>, or <c>0</c> to clear all.
    /// </summary>
    public static string DispositionText(StreamDisposition disposition)
    {
        var names = DispositionNames.Where(d => disposition.HasFlag(d.Flag)).Select(d => d.Name).ToList();
        return names.Count == 0 ? "0" : string.Join('+', names);
    }

    private static string SupportedList() => string.Join(", ", ContainerProfile.SupportedExtensions);
}
