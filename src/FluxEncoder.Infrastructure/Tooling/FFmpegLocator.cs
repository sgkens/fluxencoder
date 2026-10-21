using System.Collections.Immutable;

namespace FluxEncoder.Infrastructure.Tooling;

/// <summary>
/// Finds FFmpeg and FFprobe on Windows, macOS, and Linux.
/// </summary>
/// <remarks>
/// Search order per tool: configured override → <c>FLUXENCODER_FFMPEG</c>/<c>FLUXENCODER_FFPROBE</c> →
/// directory of the other resolved tool → application directory → <c>PATH</c> → well-known install
/// locations. An explicit override or environment variable that does not resolve stops the search for that
/// tool, so a typo never silently selects a different FFmpeg build.
/// </remarks>
public static class FFmpegLocator
{
    public const string FFmpegVariable = "FLUXENCODER_FFMPEG";
    public const string FFprobeVariable = "FLUXENCODER_FFPROBE";

    public static ToolDiscovery Locate(ToolPathOverrides overrides, ToolEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(environment);

        var searched = ImmutableArray.CreateBuilder<string>();
        var problems = ImmutableArray.CreateBuilder<string>();
        var search = new Search(environment, searched, problems);

        var ffmpegConfigured = overrides.FFmpeg ?? environment.GetVariable(FFmpegVariable);
        var ffprobeConfigured = overrides.FFprobe ?? environment.GetVariable(FFprobeVariable);

        ToolLocation? ffmpeg, ffprobe;

        // Resolve an explicitly configured tool first so the other is looked for beside it.
        if (string.IsNullOrWhiteSpace(ffmpegConfigured) && !string.IsNullOrWhiteSpace(ffprobeConfigured))
        {
            ffprobe = search.Find(FFmpegTool.FFprobe, overrides.FFprobe, FFprobeVariable, sibling: null);
            ffmpeg = search.Find(FFmpegTool.FFmpeg, overrides.FFmpeg, FFmpegVariable, sibling: ffprobe);
        }
        else
        {
            ffmpeg = search.Find(FFmpegTool.FFmpeg, overrides.FFmpeg, FFmpegVariable, sibling: null);
            ffprobe = search.Find(FFmpegTool.FFprobe, overrides.FFprobe, FFprobeVariable, sibling: ffmpeg);
        }

        return new ToolDiscovery(ffmpeg, ffprobe, searched.ToImmutable(), problems.ToImmutable(), environment.Platform);
    }

    /// <summary>
    /// The executable file name for a tool on a platform: <c>ffmpeg.exe</c> on Windows, <c>ffmpeg</c> elsewhere.
    /// </summary>
    public static string FileName(FFmpegTool tool, HostPlatform platform)
    {
        var name = tool == FFmpegTool.FFmpeg ? "ffmpeg" : "ffprobe";
        return platform == HostPlatform.Windows ? name + ".exe" : name;
    }

    /// <summary>
    /// Install directories commonly missing from <c>PATH</c> for non-interactive shells and GUI-launched terminals.
    /// </summary>
    public static IEnumerable<string> WellKnownDirectories(ToolEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        string? Under(string variable, params string[] parts) =>
            environment.GetVariable(variable) is { Length: > 0 } root ? Path.Combine([root, .. parts]) : null;

        IEnumerable<string?> candidates = environment.Platform switch
        {
            HostPlatform.Windows =>
            [
                Under("LOCALAPPDATA", "Microsoft", "WinGet", "Links"),
                Under("USERPROFILE", "scoop", "shims"),
                Under("ProgramData", "chocolatey", "bin"),
                Under("ProgramFiles", "ffmpeg", "bin"),
                environment.GetVariable("SystemDrive") is { Length: > 0 } drive ? drive + @"\ffmpeg\bin" : null,
            ],
            HostPlatform.MacOS => ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin"],
            _ => ["/usr/bin", "/usr/local/bin", "/home/linuxbrew/.linuxbrew/bin", "/snap/bin", Under("HOME", ".local", "bin")],
        };

        return candidates.OfType<string>();
    }

    private sealed class Search(ToolEnvironment environment, ImmutableArray<string>.Builder searched, ImmutableArray<string>.Builder problems)
    {
        public ToolLocation? Find(FFmpegTool tool, string? configured, string variable, ToolLocation? sibling)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Explicit(tool, configured, ToolSource.Configured, "configured path");
            }

            if (environment.GetVariable(variable) is { } fromVariable && !string.IsNullOrWhiteSpace(fromVariable))
            {
                return Explicit(tool, fromVariable, ToolSource.EnvironmentVariable, variable);
            }

            if (sibling is not null && InDirectory(tool, Path.GetDirectoryName(sibling.Path), ToolSource.Sibling) is { } nextToSibling)
            {
                return nextToSibling;
            }

            if (InDirectory(tool, environment.ApplicationDirectory, ToolSource.ApplicationDirectory) is { } bundled)
            {
                return bundled;
            }

            var separator = environment.Platform == HostPlatform.Windows ? ';' : ':';
            var pathEntries = (environment.GetVariable("PATH") ?? string.Empty)
                .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => entry.Trim('"'));

            foreach (var directory in pathEntries)
            {
                if (InDirectory(tool, directory, ToolSource.SearchPath) is { } onPath)
                {
                    return onPath;
                }
            }

            foreach (var directory in WellKnownDirectories(environment))
            {
                if (InDirectory(tool, directory, ToolSource.WellKnownLocation) is { } known)
                {
                    return known;
                }
            }

            return null;
        }

        private ToolLocation? InDirectory(FFmpegTool tool, string? directory, ToolSource source)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            var candidate = Path.Combine(directory, FileName(tool, environment.Platform));
            if (searched.Contains(candidate))
            {
                return null;
            }

            searched.Add(candidate);
            return environment.IsExecutableFile(candidate) ? new ToolLocation(tool, candidate, source) : null;
        }

        private ToolLocation? Explicit(FFmpegTool tool, string value, ToolSource source, string origin)
        {
            var path = value.Trim().Trim('"');

            if (environment.DirectoryExists(path))
            {
                var inDirectory = InDirectory(tool, path, source);
                if (inDirectory is null)
                {
                    problems.Add($"The {origin} '{path}' is a directory without {FileName(tool, environment.Platform)}.");
                }

                return inDirectory;
            }

            searched.Add(path);
            if (environment.IsExecutableFile(path))
            {
                return new ToolLocation(tool, path, source);
            }

            problems.Add($"The {origin} '{path}' does not exist or is not executable.");
            return null;
        }
    }
}
