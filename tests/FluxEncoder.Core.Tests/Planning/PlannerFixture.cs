using System.Collections.Frozen;
using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Hardware;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Tooling;

namespace FluxEncoder.Core.Tests.Planning;

/// <summary>
/// A deterministic planning environment: fixed token, POSIX paths, an in-memory file system, and a capability
/// set with the common encoders and muxers.
/// </summary>
internal static class PlannerFixture
{
    public const string Token = "tok1";

    public static readonly string[] DefaultEncoders = ["libx264", "libx265", "libaom-av1", "aac", "libopus", "ac3", "eac3", "flac", "libmp3lame"];

    public sealed class FakeFileSystem : IPlanningFileSystem
    {
        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Directories { get; } = new(StringComparer.Ordinal) { "/", "/media", "/media/out", "/tmp" };

        public HashSet<string> ReadOnly { get; } = new(StringComparer.Ordinal);

        public long? FreeSpace { get; set; } = 500L * 1024 * 1024 * 1024;

        public string GetFullPath(string path) => path.StartsWith('/') ? path : "/media/" + path;

        public bool FileExists(string path) => Files.Contains(path);

        public bool DirectoryExists(string path) => Directories.Contains(path);

        public bool IsSameFile(string first, string second) => first == second;

        public long? AvailableFreeSpace(string directory) => FreeSpace;

        public bool IsWritableDirectory(string directory) => !ReadOnly.Contains(directory);
    }

    public static FFmpegCapabilities Capabilities(params string[] encoders) => new()
    {
        Version = new FFmpegVersion("8.0.1"),
        Encoders = (encoders.Length == 0 ? DefaultEncoders : encoders)
            .ToFrozenDictionary(e => e, e => new CodecImplementation(e, MediaStreamKind.Video, e), StringComparer.Ordinal),
        Muxers = new FormatCatalog(ContainerProfile.All.Select(p => p.Muxer).Distinct().Append("null").Select(m => new FormatEntry([m], m))),
    };

    public static PlanningContext Context(FakeFileSystem? fs = null, FFmpegCapabilities? capabilities = null) => new()
    {
        Capabilities = capabilities ?? Capabilities(),
        Hardware = new CpuStrategy(),
        FileSystem = fs ?? new FakeFileSystem(),
        ScratchRoot = "/tmp",
        NullDevice = "/dev/null",
        NewToken = () => Token,
    };

    public static LanguageTag Lang(string code) =>
        LanguageTag.TryParse(code, out var tag) ? tag : throw new ArgumentException(code);

    /// <summary>
    /// A typical Blu-ray remux: HEVC HDR10 video, TrueHD and AC-3 audio, PGS and ASS subtitles, a font, cover art,
    /// and two chapters.
    /// </summary>
    public static MediaInfo Movie() => new()
    {
        Source = "/media/movie.mkv",
        FormatNames = ["matroska", "webm"],
        Duration = TimeSpan.FromMinutes(100),
        SizeBytes = 20L * 1024 * 1024 * 1024,
        Streams =
        [
            new VideoStream
            {
                Index = 0, CodecName = "hevc", Width = 3840, Height = 2160, PixelFormat = "yuv420p10le", BitDepth = 10,
                AverageFrameRate = new Rational(24000, 1001), BaseFrameRate = new Rational(24000, 1001),
                Disposition = StreamDisposition.Default,
                Color = new ColorInfo { Range = "tv", Primaries = "bt2020", Transfer = "smpte2084", Matrix = "bt2020nc" },
                Hdr = new HdrInfo
                {
                    MasteringDisplay = new MasteringDisplay
                    {
                        RedX = new(34000, 50000), RedY = new(16000, 50000), GreenX = new(13250, 50000), GreenY = new(34500, 50000),
                        BlueX = new(7500, 50000), BlueY = new(3000, 50000), WhitePointX = new(15635, 50000), WhitePointY = new(16450, 50000),
                        MinLuminance = new(1, 10000), MaxLuminance = new(10000000, 10000),
                    },
                    ContentLightLevel = new ContentLightLevel(1000, 400),
                },
                Tags = MediaTags.Create([new("BPS-eng", "60000000"), new("DURATION-eng", "01:40:00.000")]),
            },
            new AudioStream { Index = 1, CodecName = "truehd", Channels = 8, Language = Lang("eng"), Disposition = StreamDisposition.Default },
            new AudioStream { Index = 2, CodecName = "ac3", Channels = 6, Language = Lang("eng"), Title = "Commentary", Disposition = StreamDisposition.Comment },
            new SubtitleStream { Index = 3, CodecName = "hdmv_pgs_subtitle", Language = Lang("eng") },
            new SubtitleStream { Index = 4, CodecName = "ass", Language = Lang("fre"), Disposition = StreamDisposition.Forced },
            new AttachmentStream { Index = 5, FileName = "font.ttf", MimeType = "font/ttf" },
            new VideoStream { Index = 6, CodecName = "mjpeg", Disposition = StreamDisposition.AttachedPicture },
        ],
        Chapters = [new MediaChapter(TimeSpan.Zero, TimeSpan.FromMinutes(50)), new MediaChapter(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(100))],
    };

    public static PlanningInput Input(MediaInfo? media = null)
    {
        media ??= Movie();
        return new PlanningInput(InputSource.Classify(media.Source), media);
    }

    public static EncodingJob Job(string output, VideoTarget? video = null, AudioTarget? audio = null) => new()
    {
        InputPath = "/media/movie.mkv",
        OutputPath = output,
        Video = video ?? new VideoTarget.Encode(VideoCodec.Hevc, QualityPreset.Balanced),
        Audio = audio ?? new AudioTarget.StreamCopy(),
    };
}
