using System.Collections.Immutable;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Applies a job's <see cref="StreamSelection"/> to a probed input and decides each output stream's
/// handling and disposition. Pure: no FFmpeg, no file system.
/// </summary>
/// <remarks>
/// Disposition rules: source flags are preserved except <see cref="StreamDisposition.Default"/>, which is
/// recomputed so each kind has at most one default. Video and audio always get one (the source default if it
/// was kept, else the first kept; for a language list, the first preferred stream). Subtitles keep the source
/// default only if it was selected and never gain one, so no subtitle starts showing that did not before.
/// </remarks>
public static class StreamMapper
{
    public static StreamMap Map(EncodingJob job, MediaInfo media)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(media);

        var selection = job.Streams;
        var warnings = ImmutableArray.CreateBuilder<JobWarning>();
        var output = ImmutableArray.CreateBuilder<MappedStream>();

        var videoHandling = job.Video is VideoTarget.Encode ? StreamHandling.Transcode : StreamHandling.Copy;
        var audioHandling = job.Audio is AudioTarget.Encode ? StreamHandling.Transcode : StreamHandling.Copy;

        var video = SelectVideo(media.MotionVideoStreams.ToList(), selection.Video);
        AddWithSingleDefault(output, video, videoHandling, PreferredDefault(video, preferFirst: false));

        var audio = SelectTracks(media.AudioStreams.ToList(), selection.Audio, fallbackToFirst: true, out var audioMissing);
        if (audioMissing)
        {
            warnings.Add(new JobWarning(
                WarningCodes.AudioLanguageNotFound,
                $"No audio stream matches {Describe(selection.Audio)}; kept the default audio stream instead."));
        }

        var audioByPreference = selection.Audio is TrackSelection.Languages && !audioMissing;
        AddWithSingleDefault(output, audio, audioHandling, PreferredDefault(audio, audioByPreference));

        var subtitles = SelectTracks(media.SubtitleStreams.ToList(), selection.Subtitles, fallbackToFirst: false, out var subtitlesMissing);
        if (subtitlesMissing)
        {
            warnings.Add(new JobWarning(
                WarningCodes.SubtitleLanguageNotFound,
                $"No subtitle stream matches {Describe(selection.Subtitles)}; the output has no subtitles."));
        }

        AddWithSingleDefault(output, subtitles, StreamHandling.Copy, subtitles.FirstOrDefault(s => s.IsDefault));

        if (selection.CoverArt)
        {
            AddPreserved(output, media.CoverArt);
        }

        if (selection.DataStreams)
        {
            AddPreserved(output, media.Streams.OfType<DataStream>());
        }

        if (selection.Attachments)
        {
            AddPreserved(output, media.Attachments);
        }

        return new StreamMap(output.ToImmutable(), selection.Chapters, selection.Metadata, warnings.ToImmutable());
    }

    private static List<VideoStream> SelectVideo(List<VideoStream> motion, VideoSelection selection) => selection switch
    {
        VideoSelection.All => motion,
        VideoSelection.None => [],
        _ => (motion.FirstOrDefault(s => s.IsDefault) ?? motion.FirstOrDefault()) is { } primary ? [primary] : [],
    };

    private static List<T> SelectTracks<T>(List<T> streams, TrackSelection selection, bool fallbackToFirst, out bool languageMissing)
        where T : MediaStream
    {
        languageMissing = false;

        switch (selection)
        {
            case TrackSelection.All:
                return streams;

            case TrackSelection.None:
                return [];

            case TrackSelection.Languages languages:
                var matched = languages.Preferred
                    .SelectMany(tag => streams.Where(s => s.Language is { } language && tag.Matches(language)))
                    .Distinct()
                    .ToList();

                if (matched.Count > 0 || streams.Count == 0)
                {
                    return matched;
                }

                languageMissing = true;
                return fallbackToFirst ? SelectDefault(streams, fallbackToFirst) : [];

            default:
                return SelectDefault(streams, fallbackToFirst);
        }
    }

    private static List<T> SelectDefault<T>(List<T> streams, bool fallbackToFirst)
        where T : MediaStream
    {
        var chosen = streams.FirstOrDefault(s => s.IsDefault) ?? (fallbackToFirst ? streams.FirstOrDefault() : null);
        return chosen is null ? [] : [chosen];
    }

    private static MediaStream? PreferredDefault<T>(List<T> kept, bool preferFirst)
        where T : MediaStream =>
        preferFirst ? kept.FirstOrDefault() : kept.FirstOrDefault(s => s.IsDefault) ?? kept.FirstOrDefault();

    private static void AddWithSingleDefault<T>(
        ImmutableArray<MappedStream>.Builder output, List<T> kept, StreamHandling handling, MediaStream? defaultStream)
        where T : MediaStream
    {
        foreach (var stream in kept)
        {
            var disposition = ReferenceEquals(stream, defaultStream)
                ? stream.Disposition | StreamDisposition.Default
                : stream.Disposition & ~StreamDisposition.Default;

            output.Add(new MappedStream(stream, handling, disposition));
        }
    }

    private static void AddPreserved(ImmutableArray<MappedStream>.Builder output, IEnumerable<MediaStream> streams)
    {
        foreach (var stream in streams)
        {
            output.Add(new MappedStream(stream, StreamHandling.Copy, stream.Disposition));
        }
    }

    private static string Describe(TrackSelection selection) =>
        selection is TrackSelection.Languages languages
            ? $"the requested languages ({string.Join(", ", languages.Preferred)})"
            : "the selection";
}
