using System.Collections.Frozen;
using System.Collections.Immutable;
using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Planning;

/// <summary>
/// What an output container can hold, keyed by file extension. The planner checks every mapped stream against
/// the profile before FFmpeg runs, so incompatibilities surface as a clear error or a warned conversion instead
/// of a muxer failure halfway through an encode.
/// </summary>
/// <remarks>
/// Codec names are FFmpeg codec names (<c>h264</c>, <c>aac</c>, <c>hdmv_pgs_subtitle</c>). A <see langword="null"/>
/// codec set means the container accepts any codec of that kind (Matroska).
/// </remarks>
public sealed record ContainerProfile
{
    public required string Muxer { get; init; }

    public required ImmutableArray<string> Extensions { get; init; }

    public bool AudioOnly { get; init; }

    /// <summary>
    /// Accepted video codecs, or <see langword="null"/> for any.
    /// </summary>
    public FrozenSet<string>? VideoCodecs { get; init; }

    /// <summary>
    /// Accepted audio codecs, or <see langword="null"/> for any.
    /// </summary>
    public FrozenSet<string>? AudioCodecs { get; init; }

    public FrozenSet<string> SubtitleCodecs { get; init; } = FrozenSet<string>.Empty;

    /// <summary>
    /// The codec text subtitles are converted to when their own codec is not accepted, or <see langword="null"/>
    /// when the container cannot hold text subtitles at all.
    /// </summary>
    public string? TextSubtitleCodec { get; init; }

    /// <summary>
    /// The codec used when a copied audio stream is not accepted and must be re-encoded.
    /// </summary>
    public required AudioCodec FallbackAudio { get; init; }

    public bool Attachments { get; init; }

    public bool Chapters { get; init; }

    /// <summary>
    /// Cover art can be written as an attached-picture stream. Matroska is excluded: FFmpeg's Matroska muxer
    /// writes such streams as ordinary video tracks rather than image attachments.
    /// </summary>
    public bool CoverArt { get; init; }

    public bool DataStreams { get; init; }

    /// <summary>
    /// Move the MP4/MOV index to the front so playback can start before download completes.
    /// </summary>
    public bool FastStart { get; init; }

    public bool AcceptsVideo(string codec) => !AudioOnly && (VideoCodecs is null || VideoCodecs.Contains(codec));

    public bool AcceptsAudio(string codec) => AudioCodecs is null || AudioCodecs.Contains(codec);

    public bool AcceptsSubtitle(string codec) => SubtitleCodecs.Contains(codec);

    private static readonly FrozenSet<string> TextSubtitles = Set("subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text");

    private static readonly FrozenSet<string> Mp4Video = Set("h264", "hevc", "av1", "vp9", "mpeg4", "mpeg2video");

    private static readonly FrozenSet<string> Mp4Audio = Set("aac", "ac3", "eac3", "opus", "flac", "mp3", "alac");

    public static ImmutableArray<ContainerProfile> All { get; } =
    [
        new()
        {
            Muxer = "matroska",
            Extensions = [".mkv"],
            SubtitleCodecs = Set("subrip", "ass", "ssa", "webvtt", "hdmv_pgs_subtitle", "dvd_subtitle", "dvb_subtitle"),
            TextSubtitleCodec = "subrip",
            FallbackAudio = AudioCodec.Opus,
            Attachments = true,
            Chapters = true,
        },
        new()
        {
            Muxer = "matroska",
            Extensions = [".mka"],
            AudioOnly = true,
            FallbackAudio = AudioCodec.Opus,
            Attachments = true,
            Chapters = true,
        },
        new()
        {
            Muxer = "webm",
            Extensions = [".webm"],
            VideoCodecs = Set("vp8", "vp9", "av1"),
            AudioCodecs = Set("opus", "vorbis"),
            SubtitleCodecs = Set("webvtt"),
            TextSubtitleCodec = "webvtt",
            FallbackAudio = AudioCodec.Opus,
            Chapters = true,
        },
        new()
        {
            Muxer = "mp4",
            Extensions = [".mp4", ".m4v"],
            VideoCodecs = Mp4Video,
            AudioCodecs = Mp4Audio,
            SubtitleCodecs = Set("mov_text"),
            TextSubtitleCodec = "mov_text",
            FallbackAudio = AudioCodec.Aac,
            Chapters = true,
            CoverArt = true,
            DataStreams = true,
            FastStart = true,
        },
        new()
        {
            Muxer = "mov",
            Extensions = [".mov"],
            VideoCodecs = Set([.. Mp4Video, "prores", "dnxhd", "mjpeg"]),
            AudioCodecs = Set([.. Mp4Audio, "pcm_s16le", "pcm_s24le", "pcm_s16be", "pcm_s24be"]),
            SubtitleCodecs = Set("mov_text"),
            TextSubtitleCodec = "mov_text",
            FallbackAudio = AudioCodec.Aac,
            Chapters = true,
            CoverArt = true,
            DataStreams = true,
            FastStart = true,
        },
        new()
        {
            Muxer = "ipod",
            Extensions = [".m4a"],
            AudioOnly = true,
            AudioCodecs = Set("aac", "alac"),
            FallbackAudio = AudioCodec.Aac,
            Chapters = true,
            CoverArt = true,
            FastStart = true,
        },
        new()
        {
            Muxer = "mpegts",
            Extensions = [".ts", ".m2ts", ".mts"],
            VideoCodecs = Set("h264", "hevc", "mpeg2video"),
            AudioCodecs = Set("aac", "ac3", "eac3", "mp3", "mp2", "opus"),
            SubtitleCodecs = Set("dvb_subtitle"),
            FallbackAudio = AudioCodec.Aac,
        },
        new()
        {
            Muxer = "mp3",
            Extensions = [".mp3"],
            AudioOnly = true,
            AudioCodecs = Set("mp3"),
            FallbackAudio = AudioCodec.Mp3,
            CoverArt = true,
        },
        new()
        {
            Muxer = "flac",
            Extensions = [".flac"],
            AudioOnly = true,
            AudioCodecs = Set("flac"),
            FallbackAudio = AudioCodec.Flac,
            CoverArt = true,
        },
        new()
        {
            Muxer = "ogg",
            Extensions = [".opus", ".ogg", ".oga"],
            AudioOnly = true,
            AudioCodecs = Set("opus", "vorbis", "flac"),
            FallbackAudio = AudioCodec.Opus,
        },
    ];

    private static readonly FrozenDictionary<string, ContainerProfile> ByExtension =
        All.SelectMany(p => p.Extensions.Select(e => KeyValuePair.Create(e, p)))
           .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The profile for an output path's extension, or <see langword="null"/> when the extension is not supported.
    /// </summary>
    public static ContainerProfile? ForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        return extension.Length > 0 && ByExtension.TryGetValue(extension, out var profile) ? profile : null;
    }

    public static IEnumerable<string> SupportedExtensions => All.SelectMany(p => p.Extensions);

    public static bool IsTextSubtitle(string? codec) => codec is not null && TextSubtitles.Contains(codec);

    private static FrozenSet<string> Set(params string[] codecs) => codecs.ToFrozenSet(StringComparer.Ordinal);
}
