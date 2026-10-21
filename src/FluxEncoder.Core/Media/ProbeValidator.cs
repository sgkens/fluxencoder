using System.Collections.Immutable;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Media;

/// <summary>
/// Decides whether a probed input can be encoded, and what the user should be warned about.
/// </summary>
/// <remarks>
/// Rejects inputs with no audio or video, since there is nothing to encode. An unknown or zero duration on a
/// local file is a warning, not an error: the job runs as an unbounded input without percentage progress.
/// Streams FFmpeg cannot identify are warned about because they are likely to fail if mapped.
/// </remarks>
public static class ProbeValidator
{
    public static (JobFailure? Failure, ImmutableArray<JobWarning> Warnings) Validate(MediaInfo media, InputSource source)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(source);

        if (media.Streams.Count == 0)
        {
            return (new JobFailure(FailureCategory.InvalidInput, $"'{source.Value}' contains no streams."), []);
        }

        if (!media.MotionVideoStreams.Any() && !media.AudioStreams.Any())
        {
            var kinds = string.Join(", ", media.Streams.Select(s => s.Kind.ToString().ToLowerInvariant()).Distinct());
            return (new JobFailure(FailureCategory.InvalidInput, $"'{source.Value}' has no audio or video to encode (found: {kinds})."), []);
        }

        var warnings = ImmutableArray.CreateBuilder<JobWarning>();

        if (!media.HasKnownDuration && source.Kind == InputKind.LocalFile)
        {
            warnings.Add(new JobWarning(
                WarningCodes.DurationUnknown,
                "The input does not declare a duration; progress is shown without a percentage."));
        }

        foreach (var stream in media.Streams)
        {
            if (stream.CodecName is null && stream.Kind is MediaStreamKind.Video or MediaStreamKind.Audio or MediaStreamKind.Subtitle)
            {
                warnings.Add(new JobWarning(
                    WarningCodes.UnknownCodec,
                    $"Stream {stream.Index} ({stream.Kind.ToString().ToLowerInvariant()}) uses a codec this FFmpeg build cannot identify."));
            }
        }

        return (null, warnings.ToImmutable());
    }
}
