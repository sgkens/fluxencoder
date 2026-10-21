using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;

namespace FluxEncoder.Cli.Batching;

/// <summary>
/// Chooses an output path when the user gave none, or gave a directory.
/// </summary>
/// <remarks>
/// <para>
/// Name: <c>{input name}.{suffix}{extension}</c> beside the input, where the suffix is the target codec
/// (<c>hevc</c>) or <c>remux</c> for a stream copy, so the default never collides with the input and says what
/// changed: <c>movie.mkv</c> becomes <c>movie.hevc.mkv</c>. In a different output directory the suffix is
/// dropped (<c>out/movie.mkv</c>).
/// </para>
/// <para>
/// Extension: <c>--format</c> when given; otherwise the input's own container when FluxEncoder can write it;
/// otherwise <c>.mkv</c>, which holds every stream type.
/// </para>
/// </remarks>
internal static class OutputNamer
{
    public const string DefaultExtension = ".mkv";

    /// <param name="output">The <c>--output</c> value, or <see langword="null"/>.</param>
    /// <param name="format">The <c>--format</c> value without or with a leading dot, or <see langword="null"/>.</param>
    /// <param name="treatOutputAsDirectory">Several inputs share one <c>--output</c>, which must then be a directory.</param>
    public static string Resolve(string input, string? output, string? format, VideoTarget video, bool treatOutputAsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(video);

        if (output is not null && !treatOutputAsDirectory && !IsDirectoryLike(output))
        {
            return output;
        }

        var extension = Extension(input, format);
        var stem = Stem(input);
        var inputDirectory = InputSource.Classify(input).LocalPath is { } local ? Path.GetDirectoryName(Path.GetFullPath(local)) : null;
        var directory = output is null
            ? inputDirectory ?? Directory.GetCurrentDirectory()
            : Path.GetFullPath(output);

        var sameDirectory = inputDirectory is not null && PathsEqual(directory, inputDirectory);
        var name = sameDirectory || output is null ? $"{stem}.{Suffix(video)}{extension}" : stem + extension;

        return Path.Combine(directory, name);
    }

    /// <summary>
    /// An existing directory, or a path ending in a directory separator.
    /// </summary>
    public static bool IsDirectoryLike(string path) =>
        Directory.Exists(path)
        || path.EndsWith(Path.DirectorySeparatorChar)
        || path.EndsWith(Path.AltDirectorySeparatorChar);

    public static string Extension(string input, string? format)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            return format.StartsWith('.') ? format.ToLowerInvariant() : "." + format.ToLowerInvariant();
        }

        var inputExtension = Path.GetExtension(StripQuery(input));
        return inputExtension.Length > 0 && ContainerProfile.ForPath(inputExtension) is not null
            ? inputExtension.ToLowerInvariant()
            : DefaultExtension;
    }

    private static string Suffix(VideoTarget video) => video switch
    {
        VideoTarget.Encode encode => encode.Codec.ToString().ToLowerInvariant(),
        _ => "remux",
    };

    private static string Stem(string input)
    {
        var name = Path.GetFileNameWithoutExtension(StripQuery(input));
        return string.IsNullOrWhiteSpace(name) || name == "-" ? "output" : name;
    }

    private static string StripQuery(string input)
    {
        var query = input.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 && input.Contains("://", StringComparison.Ordinal) ? input[..query] : input;
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
