using System.Globalization;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// Fits a <see cref="StreamMap"/> to an output container: chooses each stream's encoder or copy, converts or drops
/// what the container cannot hold, and assigns output specifiers.
/// </summary>
/// <remarks>
/// Policy: an explicit request that cannot be met (encode HEVC into WebM, copy H.264 into WebM) is an error,
/// because silently doing something else would surprise the user. Incidental streams the user did not ask about
/// individually are adapted with a warning: copied audio the container rejects is re-encoded to the container's
/// usual codec, text subtitles are converted, and image subtitles, cover art, attachments, and data streams the
/// container cannot hold are dropped.
/// </remarks>
public static class StreamFitter
{
    public sealed record Fit(IReadOnlyList<FittedStream> Streams, bool KeepChapters, bool KeepMetadata, IReadOnlyList<JobWarning> Warnings, JobFailure? Failure);

    private static readonly string[] StatisticsTags =
        ["BPS", "DURATION", "NUMBER_OF_FRAMES", "NUMBER_OF_BYTES", "_STATISTICS_TAGS", "_STATISTICS_WRITING_APP", "_STATISTICS_WRITING_DATE_UTC"];

    public static Fit Apply(EncodingJob job, MediaInfo media, StreamMap map, ContainerProfile profile, PlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);

        var warnings = new List<JobWarning>(map.Warnings);
        var fitted = new List<FittedStream>();
        var counters = new Dictionary<char, int>();
        var extension = Path.GetExtension(job.OutputPath);
        var droppedSubtitles = new List<int>();
        var stylingLost = false;

        FittedStream Add(MappedStream mapped, char type, string codec, IReadOnlyList<KeyValuePair<string, string>> options, bool motion = false)
        {
            var index = counters.GetValueOrDefault(type);
            counters[type] = index + 1;
            var stream = new FittedStream(mapped.Source, type, index, codec, options, mapped.Disposition, motion);
            fitted.Add(stream);
            return stream;
        }

        Fit Fail(FailureCategory category, string message) => new([], false, false, warnings, new JobFailure(category, message));

        foreach (var mapped in map.Streams)
        {
            switch (mapped.Source)
            {
                case VideoStream { IsAttachedPicture: false } video:
                    if (profile.AudioOnly)
                    {
                        warnings.Add(new JobWarning(WarningCodes.VideoDropped, $"'{extension}' holds audio only; video stream {video.Index} was left out."));
                        break;
                    }

                    if (job.Video is VideoTarget.Encode encode)
                    {
                        var codecName = CodecName(encode.Codec);
                        if (!profile.AcceptsVideo(codecName))
                        {
                            return Fail(FailureCategory.Unsupported, $"{Display(encode.Codec)} video cannot be stored in '{extension}'. Use .mkv or .mp4, or choose another codec.");
                        }

                        if (!context.Hardware.TryGetVideoEncoder(encode.Codec, out var encoder) || !context.Capabilities.HasEncoder(encoder))
                        {
                            return Fail(FailureCategory.Unsupported, $"This FFmpeg build has no {encoder ?? Display(encode.Codec)} encoder.");
                        }

                        var options = new List<KeyValuePair<string, string>>(encode.TargetBitRateKbps is { } kbps
                            ? [.. context.Hardware.GetBitRateOptions(encode.Codec, encode.Quality), new("b", Kilobits(kbps))]
                            : context.Hardware.GetQualityOptions(encode.Codec, encode.Quality));
                        options.AddRange(VideoEncodeOptions.For(video, encode.Codec, encoder, context.Hardware, warnings));

                        if (context.Capabilities.Encoders.TryGetValue(encoder, out var implementation) && implementation.IsExperimental)
                        {
                            options.Add(new("strict", "experimental"));
                        }

                        if (encode.Codec == VideoCodec.Vvc && !warnings.Any(w => w.Code == WarningCodes.VvcExperimental))
                        {
                            warnings.Add(new JobWarning(WarningCodes.VvcExperimental, "VVC (H.266) encoding is experimental and few players can decode it yet."));
                        }

                        options.AddRange(ClearStatistics(video));
                        Add(mapped, 'v', encoder, options, motion: true);
                    }
                    else
                    {
                        if (video.CodecName is not { } source || !profile.AcceptsVideo(source))
                        {
                            return Fail(FailureCategory.Unsupported, $"{video.CodecName ?? "This"} video cannot be copied into '{extension}'. Re-encode it or use .mkv.");
                        }

                        Add(mapped, 'v', "copy", [], motion: true);
                    }

                    break;

                case VideoStream cover:
                    if (profile.CoverArt && cover.CodecName is "mjpeg" or "png")
                    {
                        Add(mapped, 'v', "copy", []);
                    }
                    else
                    {
                        warnings.Add(new JobWarning(WarningCodes.CoverArtDropped, $"'{extension}' cannot carry the cover art (stream {cover.Index})."));
                    }

                    break;

                case AudioStream audio:
                    var target = job.Audio as AudioTarget.Encode;
                    if (target is not null && !profile.AcceptsAudio(AudioCodecName(target.Codec)))
                    {
                        return Fail(FailureCategory.Unsupported, $"{target.Codec} audio cannot be stored in '{extension}'.");
                    }

                    if (target is null && (audio.CodecName is not { } audioCodec || !profile.AcceptsAudio(audioCodec)))
                    {
                        target = new AudioTarget.Encode(profile.FallbackAudio);
                        warnings.Add(new JobWarning(
                            WarningCodes.AudioTranscoded,
                            $"'{extension}' does not accept {audio.CodecName ?? "this"} audio; stream {audio.Index} is re-encoded to {profile.FallbackAudio}."));
                    }

                    if (target is null)
                    {
                        Add(mapped, 'a', "copy", []);
                        break;
                    }

                    var audioEncoder = AudioEncoder(target.Codec);
                    if (!context.Capabilities.HasEncoder(audioEncoder))
                    {
                        return Fail(FailureCategory.Unsupported, $"This FFmpeg build has no {audioEncoder} encoder.");
                    }

                    Add(mapped, 'a', audioEncoder, [.. AudioOptions(target, audio, warnings), .. ClearStatistics(audio)]);
                    break;

                case SubtitleStream subtitle:
                    if (subtitle.CodecName is { } subtitleCodec && profile.AcceptsSubtitle(subtitleCodec))
                    {
                        Add(mapped, 's', "copy", []);
                    }
                    else if (ContainerProfile.IsTextSubtitle(subtitle.CodecName) && profile.TextSubtitleCodec is { } textCodec)
                    {
                        stylingLost |= subtitle.CodecName is "ass" or "ssa" && textCodec != "ass";
                        Add(mapped, 's', textCodec, []);
                    }
                    else
                    {
                        droppedSubtitles.Add(subtitle.Index);
                    }

                    break;

                case AttachmentStream:
                    if (profile.Attachments)
                    {
                        Add(mapped, 't', "copy", []);
                    }
                    else if (!warnings.Any(w => w.Code == WarningCodes.AttachmentsDropped))
                    {
                        warnings.Add(new JobWarning(WarningCodes.AttachmentsDropped, $"'{extension}' cannot hold attachments such as subtitle fonts; they were left out."));
                    }

                    break;

                case DataStream data:
                    if (profile.DataStreams)
                    {
                        Add(mapped, 'd', "copy", []);
                    }
                    else
                    {
                        warnings.Add(new JobWarning(WarningCodes.DataStreamDropped, $"'{extension}' cannot hold data stream {data.Index}."));
                    }

                    break;
            }
        }

        if (droppedSubtitles.Count > 0)
        {
            warnings.Add(new JobWarning(
                WarningCodes.SubtitleDropped,
                $"'{extension}' cannot hold subtitle stream(s) {string.Join(", ", droppedSubtitles)}; image-based subtitles need .mkv."));
        }

        if (stylingLost)
        {
            warnings.Add(new JobWarning(WarningCodes.SubtitleStylingLost, $"Styled ASS/SSA subtitles were converted to {profile.TextSubtitleCodec}; fonts, colours, and positions are lost. Use .mkv to keep them."));
        }

        if (!fitted.Any(s => s.IsMotionVideo || s.Type == 'a'))
        {
            return Fail(FailureCategory.InvalidInput, $"Nothing playable is left to write to '{extension}' after applying the stream selection.");
        }

        var keepChapters = map.Chapters && profile.Chapters;
        if (map.Chapters && !profile.Chapters && media.Chapters.Count > 0)
        {
            warnings.Add(new JobWarning(WarningCodes.ChaptersDropped, $"'{extension}' cannot hold chapters."));
        }

        return new Fit(fitted, keepChapters, map.Metadata, warnings, null);
    }

    public static string CodecName(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264",
        VideoCodec.Hevc => "hevc",
        VideoCodec.Av1 => "av1",
        VideoCodec.Vvc => "vvc",
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null),
    };

    public static string AudioCodecName(AudioCodec codec) => codec switch
    {
        AudioCodec.Aac => "aac",
        AudioCodec.Opus => "opus",
        AudioCodec.Ac3 => "ac3",
        AudioCodec.Eac3 => "eac3",
        AudioCodec.Flac => "flac",
        AudioCodec.Mp3 => "mp3",
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null),
    };

    public static string AudioEncoder(AudioCodec codec) => codec switch
    {
        AudioCodec.Opus => "libopus",
        AudioCodec.Mp3 => "libmp3lame",
        _ => AudioCodecName(codec),
    };

    /// <summary>
    /// Default bit rates scale with channel count so surround audio is not starved: AAC 64 kbit/s and Opus
    /// 48 kbit/s per channel; AC-3 and E-AC-3 use their customary stereo and 5.1 rates; FLAC is lossless.
    /// MP3 is limited to two channels, so wider sources are downmixed.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> AudioOptions(AudioTarget.Encode target, AudioStream source, ICollection<JobWarning> warnings)
    {
        var channels = source.Channels ?? 2;

        if (target.Codec == AudioCodec.Mp3 && channels > 2)
        {
            warnings.Add(new JobWarning(WarningCodes.AudioDownmixed, $"MP3 holds at most two channels; stream {source.Index} ({channels} channels) is downmixed to stereo."));
            channels = 2;
            yield return new("ac", "2");
        }

        var kbps = target.BitRateKbps ?? (target.Codec switch
        {
            AudioCodec.Aac => 64 * Math.Min(channels, 8),
            AudioCodec.Opus => 48 * Math.Min(channels, 8),
            AudioCodec.Ac3 => channels <= 2 ? 192 : 448,
            AudioCodec.Eac3 => channels <= 2 ? 192 : 640,
            AudioCodec.Mp3 => 192,
            _ => (int?)null,
        });

        if (kbps is { } rate && target.Codec != AudioCodec.Flac)
        {
            yield return new("b", Kilobits(rate));
        }
    }

    /// <summary>
    /// mkvmerge writes per-stream statistics tags (bit rate, frame count). They describe the source encode and
    /// become wrong after re-encoding, so they are cleared for transcoded streams.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> ClearStatistics(MediaStream source) =>
        source.Tags.Keys
            .Where(key => StatisticsTags.Any(tag => key.Equals(tag, StringComparison.OrdinalIgnoreCase) || key.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .Select(key => KeyValuePair.Create("metadata", key + "="));

    private static string Kilobits(int kbps) => kbps.ToString(CultureInfo.InvariantCulture) + "k";

    private static string Display(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "H.264",
        VideoCodec.Hevc => "HEVC",
        VideoCodec.Av1 => "AV1",
        VideoCodec.Vvc => "VVC",
        _ => codec.ToString(),
    };
}
