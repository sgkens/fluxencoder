using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;
using FluxEncoder.Core.Security;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// Plans joining inputs end to end (TSKE005.7).
/// </summary>
/// <remarks>
/// <para>
/// Stream copy through FFmpeg's concat demuxer is only safe when every input's first video and first audio stream
/// agree on codec, dimensions, pixel format, sample aspect ratio, frame rate, sample rate, and channel layout;
/// otherwise the result plays with glitches or stops at the first boundary. <see cref="CompatibilityProblems"/>
/// checks exactly that. Copy uses an <c>ffconcat</c> list written to the job's scratch directory.
/// </para>
/// <para>
/// Re-encoding uses the <c>concat</c> filter after normalizing every input to the first one: scaled to fit and
/// padded to its size, square pixels, its frame rate, and 48 kHz stereo audio. Inputs without audio get silence of
/// their own length, so audio stays in sync across boundaries.
/// </para>
/// </remarks>
public static class ConcatPlanner
{
    public static Result<PlannedJob> Plan(ConcatJob job, IReadOnlyList<PlanningInput> inputs, PlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(context);

        if (inputs.Count != job.Inputs.Length)
        {
            throw new ArgumentException("Every job input needs exactly one probed input.", nameof(inputs));
        }

        var warnings = new List<JobWarning>();

        var profile = ContainerProfile.ForPath(job.OutputPath);
        if (profile is null || profile.AudioOnly)
        {
            return Fail(FailureCategory.InvalidUsage, $"Concatenation writes video; '{Path.GetExtension(job.OutputPath)}' is not a supported video format.");
        }

        var durations = new List<TimeSpan>();
        foreach (var input in inputs)
        {
            if (context.Policy.CheckInput(input.Source, context.FileSystem.GetFullPath) is { } denied)
            {
                return denied;
            }

            if (!input.IsProbed || !input.Source.IsSeekable || !input.Media.HasKnownDuration)
            {
                return Fail(FailureCategory.InvalidInput, $"'{input.Source.Value}' must be a local file with a known length to be joined.");
            }

            if (!input.Media.MotionVideoStreams.Any())
            {
                return Fail(FailureCategory.InvalidInput, $"'{input.Source.Value}' has no video stream.");
            }

            durations.Add(input.Media.Duration!.Value);
        }

        var problems = CompatibilityProblems(inputs);
        var firstVideo = inputs[0].Media.MotionVideoStreams.First();
        var firstAudio = inputs[0].Media.AudioStreams.FirstOrDefault();
        var containerAccepts = firstVideo.CodecName is { } v && profile.AcceptsVideo(v)
            && (firstAudio is null || firstAudio.CodecName is { } a && profile.AcceptsAudio(a));

        var copy = job.Mode switch
        {
            ConcatMode.Copy => true,
            ConcatMode.Reencode => false,
            _ => problems.Count == 0 && containerAccepts && job.Video is VideoTarget.StreamCopy,
        };

        if (copy && (problems.Count > 0 || !containerAccepts))
        {
            var reasons = problems.Count > 0 ? string.Join("; ", problems) : $"'{Path.GetExtension(job.OutputPath)}' does not accept the input codecs";
            return Fail(FailureCategory.Unsupported, $"The inputs cannot be joined without re-encoding: {reasons}.");
        }

        if (!copy && job.Mode == ConcatMode.Auto && job.Video is VideoTarget.StreamCopy)
        {
            warnings.Add(new JobWarning(
                WarningCodes.ConcatReencoded,
                problems.Count > 0
                    ? $"The inputs differ ({string.Join("; ", problems)}), so they are re-encoded to H.264."
                    : $"'{Path.GetExtension(job.OutputPath)}' does not accept the input codecs, so they are re-encoded to H.264."));
        }

        if (inputs.Any(i => i.Media.MotionVideoStreams.Count() > 1 || i.Media.AudioStreams.Count() > 1 || i.Media.SubtitleStreams.Any()))
        {
            warnings.Add(new JobWarning(
                WarningCodes.ConcatExtraStreamsDropped,
                "Concatenation joins only the first video and first audio stream of each input; extra video, audio, or subtitle streams were left out."));
        }

        var encodingJob = job.ToEncodingJob();
        var total = durations.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);
        var timeline = new InputTimeline.Finite(total);
        var estimate = copy ? inputs.Sum(i => i.Media.SizeBytes ?? 0) : 0;

        var path = OutputPathPlanner.Resolve(job.OutputPath, job.Policy, [.. inputs.Select(i => i.Source)], estimate, context);
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
        var scratch = Path.Combine(context.ScratchRoot, "flux-" + token);

        List<string> arguments;
        var prepared = ImmutableDictionary<string, string>.Empty;

        if (copy)
        {
            var list = Path.Combine(scratch, "inputs.ffconcat");
            prepared = prepared.Add(list, ConcatList(inputs.Select(i => context.FileSystem.GetFullPath(i.Source.LocalPath!))));
            // The list names absolute local paths only; the whitelist keeps it from naming anything else.
            arguments = ["-f", "concat", "-safe", "0", "-protocol_whitelist", SourcePolicy.LocalWhitelist, "-i", list, "-map", "0:v:0"];
            if (firstAudio is not null)
            {
                arguments.AddRange(["-map", "0:a:0?"]);
            }

            arguments.AddRange(["-c", "copy"]);
        }
        else
        {
            var reencode = Reencode(job, inputs, durations, profile, context, warnings);
            if (reencode.Failure is not null)
            {
                return reencode.Failure;
            }

            arguments = reencode.Arguments!;
        }

        if (!job.KeepMetadata)
        {
            arguments.AddRange(["-map_metadata:g", "-1"]);
        }
        else if (!job.KeepPrivateMetadata)
        {
            var removed = inputs
                .SelectMany(i => MetadataPrivacy.PrivateKeys(i.Media.Tags))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (removed.Count > 0)
            {
                foreach (var key in removed)
                {
                    arguments.AddRange(["-metadata:g", key + "="]);
                }

                warnings.Add(new JobWarning(
                    WarningCodes.PrivateMetadataRemoved,
                    $"Removed location and device tags ({string.Join(", ", removed)}); --keep-private-metadata keeps them."));
            }
        }

        if (!job.KeepChapters || !profile.Chapters)
        {
            arguments.AddRange(["-map_chapters", "-1"]);
            if (job.KeepChapters && !profile.Chapters && inputs.Any(i => i.Media.Chapters.Count > 0))
            {
                warnings.Add(new JobWarning(
                    WarningCodes.ChaptersDropped,
                    $"'{Path.GetExtension(job.OutputPath)}' cannot hold chapters."));
            }
        }

        if (profile.FastStart)
        {
            arguments.AddRange(["-movflags", "+faststart"]);
        }

        arguments.AddRange(EncodePlanner.LimitArguments(context.Limits));
        arguments.AddRange(["-n", "-f", profile.Muxer, temporary]);

        var plan = new OutputPlan(encodingJob, [new FFmpegInvocation(arguments)])
        {
            FinalOutputPath = final,
            TemporaryOutputPath = temporary,
            ScratchDirectory = scratch,
            PreparedFiles = prepared,
            MaxOutputBytes = context.Limits.MaxOutputBytes,
        };

        return Result<PlannedJob>.Success(PlannedJob.Run(plan, timeline, warnings));

        Result<PlannedJob> Fail(FailureCategory category, string message) => new JobFailure(category, message);
    }

    /// <summary>
    /// Differences that make stream-copy concatenation unsafe, described for the user. Empty when copy is safe.
    /// </summary>
    public static IReadOnlyList<string> CompatibilityProblems(IReadOnlyList<PlanningInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var problems = new List<string>();
        var reference = inputs[0].Media;
        var refVideo = reference.MotionVideoStreams.FirstOrDefault();
        var refAudio = reference.AudioStreams.FirstOrDefault();

        for (var i = 1; i < inputs.Count; i++)
        {
            var name = Path.GetFileName(inputs[i].Source.Value);
            var video = inputs[i].Media.MotionVideoStreams.FirstOrDefault();
            var audio = inputs[i].Media.AudioStreams.FirstOrDefault();

            void Compare<T>(string what, T expected, T actual)
            {
                if (!EqualityComparer<T>.Default.Equals(expected, actual))
                {
                    problems.Add($"{name} has {what} {actual?.ToString() ?? "unknown"} instead of {expected?.ToString() ?? "unknown"}");
                }
            }

            if (refVideo is not null && video is not null)
            {
                Compare("video codec", refVideo.CodecName, video.CodecName);
                Compare("width", refVideo.Width, video.Width);
                Compare("height", refVideo.Height, video.Height);
                Compare("pixel format", refVideo.PixelFormat, video.PixelFormat);
                Compare("sample aspect ratio", refVideo.SampleAspectRatio, video.SampleAspectRatio);
                Compare("frame rate", refVideo.AverageFrameRate, video.AverageFrameRate);
                Compare("rotation", refVideo.Rotation ?? 0, video.Rotation ?? 0);
                Compare("HDR format", refVideo.HdrFormats, video.HdrFormats);
            }

            if ((refAudio is null) != (audio is null))
            {
                problems.Add(audio is null ? $"{name} has no audio" : $"{name} has audio but the first input does not");
            }
            else if (refAudio is not null && audio is not null)
            {
                Compare("audio codec", refAudio.CodecName, audio.CodecName);
                Compare("sample rate", refAudio.SampleRate, audio.SampleRate);
                Compare("channel count", refAudio.Channels, audio.Channels);
                if (refAudio.ChannelLayout is not null && audio.ChannelLayout is not null)
                {
                    Compare("channel layout", refAudio.ChannelLayout, audio.ChannelLayout);
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// An <c>ffconcat</c> list. Paths are single-quoted with embedded quotes escaped as <c>'\''</c>.
    /// </summary>
    public static string ConcatList(IEnumerable<string> paths)
    {
        var builder = new StringBuilder("ffconcat version 1.0\n");
        foreach (var path in paths)
        {
            builder.Append("file '").Append(path.Replace("'", @"'\''", StringComparison.Ordinal)).Append("'\n");
        }

        return builder.ToString();
    }

    private sealed record ReencodePlan(List<string>? Arguments, JobFailure? Failure);

    private static ReencodePlan Reencode(
        ConcatJob job, IReadOnlyList<PlanningInput> inputs, List<TimeSpan> durations, ContainerProfile profile, PlanningContext context, List<JobWarning> warnings)
    {
        var video = job.Video as VideoTarget.Encode ?? new VideoTarget.Encode(VideoCodec.H264, QualityPreset.Balanced);
        if (!profile.AcceptsVideo(StreamFitter.CodecName(video.Codec)))
        {
            return new(null, new JobFailure(FailureCategory.Unsupported, $"{video.Codec} video cannot be stored in '{Path.GetExtension(job.OutputPath)}'."));
        }

        if (!context.Hardware.TryGetVideoEncoder(video.Codec, out var encoder) || !context.Capabilities.HasEncoder(encoder))
        {
            return new(null, new JobFailure(FailureCategory.Unsupported, $"This FFmpeg build has no {encoder ?? video.Codec.ToString()} encoder."));
        }

        var audioTarget = job.Audio as AudioTarget.Encode ?? new AudioTarget.Encode(profile.FallbackAudio);
        var audioEncoder = StreamFitter.AudioEncoder(audioTarget.Codec);
        if (!profile.AcceptsAudio(StreamFitter.AudioCodecName(audioTarget.Codec)) || !context.Capabilities.HasEncoder(audioEncoder))
        {
            return new(null, new JobFailure(FailureCategory.Unsupported, $"{audioTarget.Codec} audio cannot be written to '{Path.GetExtension(job.OutputPath)}' with this FFmpeg build."));
        }

        var first = inputs[0].Media.MotionVideoStreams.First();
        var width = Even(first.Width ?? 1920);
        var height = Even(first.Height ?? 1080);
        var rate = first.AverageFrameRate ?? new Rational(30, 1);
        var anyAudio = inputs.Any(i => i.Media.AudioStreams.Any());
        var deep = inputs.Any(i => i.Media.MotionVideoStreams.First().BitDepth > 8) && video.Codec != VideoCodec.H264;

        var arguments = new List<string>();
        foreach (var input in inputs)
        {
            arguments.AddRange(SourcePolicy.FFmpegInput(input.Source, context.FileSystem.GetFullPath));
        }

        // Silence for inputs without audio, one lavfi input each, numbered after the real inputs.
        var silence = new Dictionary<int, int>();
        if (anyAudio)
        {
            for (var i = 0; i < inputs.Count; i++)
            {
                if (!inputs[i].Media.AudioStreams.Any())
                {
                    silence[i] = inputs.Count + silence.Count;
                    arguments.AddRange(["-f", "lavfi", "-t", EncodePlanner.FormatSeconds(durations[i]), "-i", "anullsrc=r=48000:cl=stereo"]);
                }
            }
        }

        var filter = new StringBuilder();
        var pixelFormat = deep ? "yuv420p10le" : "yuv420p";
        for (var i = 0; i < inputs.Count; i++)
        {
            filter.Append(CultureInfo.InvariantCulture,
                $"[{i}:v:0]scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={rate},format={pixelFormat}[v{i}];");

            if (anyAudio)
            {
                var source = silence.TryGetValue(i, out var silent) ? $"{silent}:a:0" : $"{i}:a:0";
                filter.Append(CultureInfo.InvariantCulture, $"[{source}]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
            }
        }

        for (var i = 0; i < inputs.Count; i++)
        {
            filter.Append(CultureInfo.InvariantCulture, $"[v{i}]");
            if (anyAudio)
            {
                filter.Append(CultureInfo.InvariantCulture, $"[a{i}]");
            }
        }

        filter.Append(CultureInfo.InvariantCulture, $"concat=n={inputs.Count}:v=1:a={(anyAudio ? 1 : 0)}[v]");
        if (anyAudio)
        {
            filter.Append("[a]");
        }

        arguments.AddRange(["-filter_complex", filter.ToString(), "-map", "[v]"]);
        if (anyAudio)
        {
            arguments.AddRange(["-map", "[a]"]);
        }

        arguments.AddRange(["-c:v", encoder]);
        var options = video.TargetBitRateKbps is { } kbps
            ? [.. context.Hardware.GetBitRateOptions(video.Codec, video.Quality), new("b", kbps.ToString(CultureInfo.InvariantCulture) + "k")]
            : context.Hardware.GetQualityOptions(video.Codec, video.Quality);
        foreach (var (name, value) in options)
        {
            arguments.AddRange([$"-{name}:v", value]);
        }

        if (anyAudio)
        {
            arguments.AddRange(["-c:a", audioEncoder]);
            if (audioTarget.Codec != AudioCodec.Flac)
            {
                var rateKbps = audioTarget.BitRateKbps ?? (audioTarget.Codec == AudioCodec.Opus ? 96 : 128);
                arguments.AddRange(["-b:a", rateKbps.ToString(CultureInfo.InvariantCulture) + "k"]);
            }
        }

        if (inputs.Any(i => i.Media.MotionVideoStreams.First().HdrFormats != HdrFormat.None))
        {
            warnings.Add(new JobWarning(WarningCodes.ConcatHdrNotPreserved, "Joining re-encodes through a filter that does not carry HDR metadata; HDR inputs come out as SDR-tagged video."));
        }

        return new(arguments, null);
    }

    private static int Even(int value) => value % 2 == 0 ? value : value + 1;
}
