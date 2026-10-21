namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Pure path rules for temporary and renamed outputs. File-system checks are passed in, so these rules are
/// testable without touching disk.
/// </summary>
public static class OutputPaths
{
    public const int MaxRenameAttempts = 9999;

    private const string PartialMarker = "partial";

    /// <summary>
    /// The temporary path FFmpeg writes to before the result is moved to <paramref name="outputPath"/>:
    /// <c>dir/.name.{token}.partial.ext</c>.
    /// </summary>
    /// <remarks>
    /// It sits in the destination directory so the final move is a same-volume rename, and it keeps the
    /// destination extension last so FFmpeg still infers the right muxer. The leading dot hides it on Unix.
    /// </remarks>
    /// <param name="token">A short per-job identifier (ASCII letters and digits) that keeps parallel jobs apart.</param>
    public static string Temporary(string outputPath, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (!token.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("Token must be ASCII letters and digits.", nameof(token));
        }

        var (directory, stem, extension) = Split(outputPath);
        return Path.Combine(directory, $".{stem}.{token}.{PartialMarker}{extension}");
    }

    /// <summary>
    /// The <c>name (n).ext</c> sibling used by <see cref="OverwritePolicy.Rename"/>.
    /// </summary>
    public static string Numbered(string outputPath, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        var (directory, stem, extension) = Split(outputPath);
        return Path.Combine(directory, $"{stem} ({number}){extension}");
    }

    /// <summary>
    /// Returns <paramref name="outputPath"/> if it is free, otherwise the first free numbered sibling.
    /// </summary>
    /// <param name="isTaken">Whether a path already exists or is reserved by another job in the batch.</param>
    /// <returns><see langword="null"/> when every candidate up to <see cref="MaxRenameAttempts"/> is taken.</returns>
    public static string? FirstAvailable(string outputPath, Func<string, bool> isTaken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(isTaken);

        if (!isTaken(outputPath))
        {
            return outputPath;
        }

        for (var n = 1; n <= MaxRenameAttempts; n++)
        {
            var candidate = Numbered(outputPath, n);
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static (string Directory, string Stem, string Extension) Split(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Length == 0)
        {
            throw new ArgumentException($"'{path}' does not name a file.", nameof(path));
        }

        return (Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(fileName), Path.GetExtension(fileName));
    }
}
