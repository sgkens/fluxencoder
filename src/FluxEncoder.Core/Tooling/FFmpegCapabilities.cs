using System.Collections.Frozen;
using System.Collections.Immutable;

namespace FluxEncoder.Core.Tooling;

/// <summary>
/// What the installed FFmpeg build can do: its version, configure flags, codecs, formats, protocols, filters,
/// and hardware acceleration methods. Planning and hardware selection check this instead of assuming a
/// feature exists.
/// </summary>
/// <remarks>
/// All name lookups are case-sensitive, as FFmpeg's own option parsing is.
/// </remarks>
public sealed record FFmpegCapabilities
{
    public required FFmpegVersion Version { get; init; }

    /// <summary>
    /// The <c>./configure</c> flags the build used, such as <c>--enable-libx265</c>.
    /// </summary>
    public ImmutableArray<string> Configuration { get; init; } = [];

    public FrozenDictionary<string, CodecImplementation> Encoders { get; init; } = FrozenDictionary<string, CodecImplementation>.Empty;

    public FrozenDictionary<string, CodecImplementation> Decoders { get; init; } = FrozenDictionary<string, CodecImplementation>.Empty;

    public ImmutableArray<FormatEntry> Muxers { get; init; } = [];

    public ImmutableArray<FormatEntry> Demuxers { get; init; } = [];

    public FrozenSet<string> InputProtocols { get; init; } = FrozenSet<string>.Empty;

    public FrozenSet<string> OutputProtocols { get; init; } = FrozenSet<string>.Empty;

    public FrozenSet<string> Filters { get; init; } = FrozenSet<string>.Empty;

    /// <summary>
    /// Methods from <c>ffmpeg -hwaccels</c>, such as <c>cuda</c>, <c>vaapi</c>, <c>qsv</c>, <c>videotoolbox</c>.
    /// Listed means compiled in, not that a device is present.
    /// </summary>
    public ImmutableArray<string> HardwareAccelerators { get; init; } = [];

    public bool HasEncoder(string name) => Encoders.ContainsKey(name);

    public bool HasDecoder(string name) => Decoders.ContainsKey(name);

    public bool HasFilter(string name) => Filters.Contains(name);

    public bool IsEnabled(string configureFlag) => Configuration.Contains(configureFlag, StringComparer.Ordinal);

    /// <summary>
    /// Encoders that produce <paramref name="codec"/> (for example <c>hevc</c> gives <c>libx265</c>,
    /// <c>hevc_nvenc</c>, <c>hevc_qsv</c>), ordered by name.
    /// </summary>
    public IEnumerable<CodecImplementation> EncodersFor(string codec) =>
        Encoders.Values.Where(e => string.Equals(e.Codec, codec, StringComparison.Ordinal)).OrderBy(e => e.Name, StringComparer.Ordinal);
}
