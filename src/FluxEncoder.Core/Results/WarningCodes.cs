namespace FluxEncoder.Core.Results;

/// <summary>
/// Released <see cref="JobWarning.Code"/> values. Codes are part of the JSON contract: add new ones, never
/// rename or reuse them.
/// </summary>
public static class WarningCodes
{
    /// <summary>
    /// An unbounded input was stopped by the user and the output was finalized.
    /// </summary>
    public const string StoppedByUser = "stopped-by-user";

    /// <summary>
    /// No audio stream matched the requested languages; the default audio stream was kept instead.
    /// </summary>
    public const string AudioLanguageNotFound = "audio-language-not-found";

    /// <summary>
    /// No subtitle stream matched the requested languages; the output has no subtitles.
    /// </summary>
    public const string SubtitleLanguageNotFound = "subtitle-language-not-found";

    /// <summary>
    /// A local file without a usable duration; it is processed like an unbounded input.
    /// </summary>
    public const string DurationUnknown = "duration-unknown";

    /// <summary>
    /// FFmpeg could not identify a stream's codec.
    /// </summary>
    public const string UnknownCodec = "unknown-codec";

    /// <summary>
    /// The input is a pipe and was not probed, so stream selection cannot be applied in advance.
    /// </summary>
    public const string InputNotProbed = "input-not-probed";

    /// <summary>
    /// A failed or cancelled job's temporary output was kept, as requested by the partial-output policy.
    /// </summary>
    public const string PartialOutputKept = "partial-output-kept";

    /// <summary>
    /// A temporary file or scratch directory could not be removed.
    /// </summary>
    public const string CleanupFailed = "cleanup-failed";

    /// <summary>
    /// The output existed, so a numbered name was used (rename policy).
    /// </summary>
    public const string OutputRenamed = "output-renamed";

    /// <summary>
    /// Free space on the output volume may not hold the result.
    /// </summary>
    public const string LowDiskSpace = "low-disk-space";

    /// <summary>
    /// The output container holds audio only, so video was left out.
    /// </summary>
    public const string VideoDropped = "video-dropped";

    /// <summary>
    /// A copied audio stream is not accepted by the container and was re-encoded.
    /// </summary>
    public const string AudioTranscoded = "audio-transcoded";

    /// <summary>
    /// The target audio codec supports fewer channels than the source; audio was downmixed.
    /// </summary>
    public const string AudioDownmixed = "audio-downmixed";

    /// <summary>
    /// Styled (ASS/SSA) subtitles were converted to a plain text format the container accepts.
    /// </summary>
    public const string SubtitleStylingLost = "subtitle-styling-lost";

    /// <summary>
    /// Subtitles the container cannot hold (usually image-based ones) were left out.
    /// </summary>
    public const string SubtitleDropped = "subtitle-dropped";

    /// <summary>
    /// The output container cannot carry the cover art.
    /// </summary>
    public const string CoverArtDropped = "cover-art-dropped";

    /// <summary>
    /// Attachments such as subtitle fonts were left out because the container cannot hold them.
    /// </summary>
    public const string AttachmentsDropped = "attachments-dropped";

    /// <summary>
    /// A data stream (timecode, telemetry) was left out because the container cannot hold it.
    /// </summary>
    public const string DataStreamDropped = "data-stream-dropped";

    /// <summary>
    /// The output container cannot hold chapters.
    /// </summary>
    public const string ChaptersDropped = "chapters-dropped";

    /// <summary>
    /// The trim end is past the end of the input; encoding stops at the input's end.
    /// </summary>
    public const string TrimEndBeyondInput = "trim-end-beyond-input";

    /// <summary>
    /// Video is stream-copied, so the start snaps to the nearest earlier keyframe.
    /// </summary>
    public const string TrimKeyframeAligned = "trim-keyframe-aligned";

    /// <summary>
    /// The source has more than 8 bits per sample; the target encode is 8-bit.
    /// </summary>
    public const string BitDepthReduced = "bit-depth-reduced";

    /// <summary>
    /// HDR in 8-bit H.264 bands visibly and few players handle it; HEVC or AV1 keep HDR properly.
    /// </summary>
    public const string HdrInH264 = "hdr-h264-limited";

    /// <summary>
    /// Re-encoding removes the Dolby Vision layer; the HDR10 base is kept where present.
    /// </summary>
    public const string DolbyVisionLost = "dolby-vision-lost";

    /// <summary>
    /// Re-encoding removes HDR10+ dynamic metadata; static HDR10 metadata is kept.
    /// </summary>
    public const string Hdr10PlusLost = "hdr10plus-lost";

    /// <summary>
    /// The inputs could not be joined by stream copy and were re-encoded.
    /// </summary>
    public const string ConcatReencoded = "concat-reencoded";

    /// <summary>
    /// Joining by re-encode does not carry HDR metadata.
    /// </summary>
    public const string ConcatHdrNotPreserved = "concat-hdr-not-preserved";

    /// <summary>
    /// HDR video was tone-mapped to SDR for the thumbnail sheet.
    /// </summary>
    public const string ThumbnailsToneMapped = "thumbnails-tone-mapped";

    /// <summary>
    /// HDR video could not be tone-mapped (no zscale/tonemap filters), so thumbnails look washed out.
    /// </summary>
    public const string ThumbnailsHdrWashedOut = "thumbnails-hdr-washed-out";

    /// <summary>
    /// The selected hardware encoder could not be used, so the CPU encoded instead.
    /// </summary>
    public const string HardwareFallback = "hardware-fallback";

    /// <summary>
    /// The encoder cannot write 10-bit here, so HDR video is encoded 8-bit and will band.
    /// </summary>
    public const string HdrNotPreserved = "hdr-not-preserved";

    /// <summary>
    /// Hardware encoders may not write HDR10 mastering metadata; the CPU encoder guarantees it.
    /// </summary>
    public const string HardwareHdrMetadata = "hardware-hdr-metadata";

    /// <summary>
    /// VVC (H.266) encoding is experimental and few players can decode it yet.
    /// </summary>
    public const string VvcExperimental = "vvc-experimental";
}
