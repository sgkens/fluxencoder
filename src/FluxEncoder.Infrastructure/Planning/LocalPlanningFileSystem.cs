using FluxEncoder.Core.Planning;

namespace FluxEncoder.Infrastructure.Planning;

/// <summary>
/// <see cref="IPlanningFileSystem"/> over the real file system.
/// </summary>
/// <remarks>
/// Path identity resolves symbolic links and compares case-insensitively on Windows and macOS, whose default file
/// systems ignore case. Hard links to the same file are not detected; .NET exposes no portable file identity.
/// </remarks>
public sealed class LocalPlanningFileSystem : IPlanningFileSystem
{
    public static LocalPlanningFileSystem Instance { get; } = new();

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsSameFile(string first, string second) =>
        string.Equals(Resolve(first), Resolve(second), PathComparison);

    public long? AvailableFreeSpace(string directory)
    {
        try
        {
            var full = Path.GetFullPath(directory);

            // The volume is the mount point with the longest root that contains the path; on Unix the path root
            // is always "/", which would report the wrong volume for anything mounted elsewhere.
            var drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && full.StartsWith(d.RootDirectory.FullName, PathComparison))
                .MaxBy(d => d.RootDirectory.FullName.Length);

            return drive?.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsWritableDirectory(string directory)
    {
        var probe = Path.Combine(directory, $".flux-write-test-{Guid.NewGuid():N}");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            return new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return full;
        }
    }
}
