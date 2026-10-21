using System.Collections.Frozen;
using System.Collections.Immutable;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Security;

/// <summary>
/// Which inputs FluxEncoder may read and where it may read and write files (TSKE010.2).
/// </summary>
/// <remarks>
/// <para>
/// Every input is passed to FFmpeg with a <c>-protocol_whitelist</c> matching its own scheme
/// (<see cref="ProtocolWhitelist"/>). Without it, a network playlist can make FFmpeg open any protocol it lists,
/// including <c>file:</c> on the local machine; with it, an HTTPS input can only ever cause HTTPS, TCP, and TLS
/// connections. Local files are always passed as absolute paths, because FFmpeg reads a relative name such as
/// <c>http:x.mkv</c> or <c>concat:a|b</c> as a protocol (<see cref="FFmpegInput"/>).
/// </para>
/// <para>
/// The default allows local files, pipes, capture devices, and the common streaming and file-transfer protocols.
/// <see cref="AllowedRoots"/> confines local inputs and outputs to listed directories, for service deployments.
/// </para>
/// </remarks>
public sealed record SourcePolicy
{
    private static readonly FrozenDictionary<string, string> Whitelists = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["http"] = "http,https,tcp,tls,crypto,httpproxy",
        ["https"] = "http,https,tcp,tls,crypto,httpproxy",
        ["rtmp"] = "rtmp,rtmps,rtmpt,rtmpts,rtmpe,rtmpte,tcp,tls,http,https",
        ["rtmps"] = "rtmp,rtmps,rtmpt,rtmpts,rtmpe,rtmpte,tcp,tls,http,https",
        ["rtmpt"] = "rtmp,rtmps,rtmpt,rtmpts,rtmpe,rtmpte,tcp,tls,http,https",
        ["rtmpts"] = "rtmp,rtmps,rtmpt,rtmpts,rtmpe,rtmpte,tcp,tls,http,https",
        ["rtsp"] = "rtsp,rtsps,rtp,srtp,udp,tcp,tls,http,https",
        ["rtsps"] = "rtsp,rtsps,rtp,srtp,udp,tcp,tls,http,https",
        ["srt"] = "srt,udp",
        ["udp"] = "udp,rtp",
        ["rtp"] = "udp,rtp",
        ["tcp"] = "tcp",
        ["ftp"] = "ftp,tcp",
        ["sftp"] = "sftp",
        ["smb"] = "smb",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Whitelist for local files: the file itself plus what FFmpeg's own local playlist handling needs.
    /// </summary>
    public const string LocalWhitelist = "file,crypto,data";

    public static SourcePolicy Default { get; } = new();

    public bool AllowLocalFiles { get; init; } = true;

    public bool AllowPipes { get; init; } = true;

    public bool AllowDevices { get; init; } = true;

    /// <summary>
    /// URL schemes allowed for network inputs, lower case. Only schemes with a known whitelist can be allowed.
    /// </summary>
    public ImmutableHashSet<string> AllowedSchemes { get; init; } = [.. Whitelists.Keys];

    /// <summary>
    /// When non-empty, local inputs and outputs must lie inside one of these absolute directories.
    /// </summary>
    public ImmutableArray<string> AllowedRoots { get; init; } = [];

    /// <summary>
    /// Whether <c>--ffmpeg-arg</c> may pass raw arguments to FFmpeg (TSKE010.1). Raw arguments bypass every rule
    /// here: FFmpeg options can open other files and network locations.
    /// </summary>
    public bool AllowExtraArguments { get; init; } = true;

    /// <summary>
    /// Checks an input against the policy.
    /// </summary>
    /// <param name="fullPath">Resolves a local path to an absolute one.</param>
    public JobFailure? CheckInput(InputSource source, Func<string, string> fullPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fullPath);

        return source.Kind switch
        {
            InputKind.LocalFile when !AllowLocalFiles => Deny("Local files are not allowed as input here."),
            InputKind.LocalFile => CheckRoot(fullPath(source.LocalPath!), "Input"),
            InputKind.Pipe when !AllowPipes => Deny("Pipe input is not allowed here."),
            InputKind.Device when !AllowDevices => Deny("Capture devices are not allowed as input here."),
            InputKind.Network when source.Scheme is null || !AllowedSchemes.Contains(source.Scheme) || !Whitelists.ContainsKey(source.Scheme) =>
                Deny($"The '{source.Scheme}' protocol is not allowed. Allowed: {string.Join(", ", AllowedSchemes.Order(StringComparer.Ordinal))}."),
            _ => null,
        };
    }

    /// <summary>
    /// Checks that an output file lies inside <see cref="AllowedRoots"/>, when roots are set.
    /// </summary>
    public JobFailure? CheckOutput(string fullOutputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullOutputPath);
        return CheckRoot(fullOutputPath, "Output");
    }

    /// <summary>
    /// The <c>-protocol_whitelist</c> value for an input, or <see langword="null"/> for capture devices, which are
    /// opened by their input device driver rather than a protocol.
    /// </summary>
    public static string? ProtocolWhitelist(InputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Kind switch
        {
            InputKind.LocalFile => LocalWhitelist,
            InputKind.Pipe => "pipe",
            InputKind.Network when source.Scheme is { } scheme && Whitelists.TryGetValue(scheme, out var list) => list,
            _ => null,
        };
    }

    /// <summary>
    /// The arguments that open <paramref name="source"/> in FFmpeg: its protocol whitelist, then <c>-i</c> with local
    /// files as absolute paths so their names are never read as protocols.
    /// </summary>
    public static IReadOnlyList<string> FFmpegInput(InputSource source, Func<string, string> fullPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fullPath);

        var arguments = new List<string>(4);
        if (ProtocolWhitelist(source) is { } whitelist)
        {
            arguments.AddRange(["-protocol_whitelist", whitelist]);
        }

        arguments.AddRange(["-i", source.Kind == InputKind.LocalFile ? fullPath(source.LocalPath!) : source.Value]);
        return arguments;
    }

    private JobFailure? CheckRoot(string fullPath, string what)
    {
        if (AllowedRoots.IsEmpty)
        {
            return null;
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var inside = AllowedRoots.Any(root =>
        {
            var normalized = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(normalized, comparison);
        });

        return inside ? null : Deny($"{what} '{fullPath}' is outside the allowed folders ({string.Join(", ", AllowedRoots)}).");
    }

    private static JobFailure Deny(string message) => new(FailureCategory.PolicyDenied, message);

    public bool Equals(SourcePolicy? other) =>
        other is not null
        && AllowLocalFiles == other.AllowLocalFiles && AllowPipes == other.AllowPipes && AllowDevices == other.AllowDevices
        && AllowExtraArguments == other.AllowExtraArguments
        && AllowedSchemes.SetEquals(other.AllowedSchemes) && AllowedRoots.SequenceEqual(other.AllowedRoots);

    public override int GetHashCode() => HashCode.Combine(AllowLocalFiles, AllowPipes, AllowDevices, AllowExtraArguments, AllowedSchemes.Count, AllowedRoots.Length);
}
