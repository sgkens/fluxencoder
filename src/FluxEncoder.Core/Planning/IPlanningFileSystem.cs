namespace FluxEncoder.Core.Planning;

/// <summary>
/// The file-system facts the planner needs. Core stays free of I/O; Infrastructure supplies the real
/// implementation and tests supply fakes.
/// </summary>
public interface IPlanningFileSystem
{
    string GetFullPath(string path);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>
    /// Whether two paths name the same file, accounting for case-insensitive file systems and symbolic links.
    /// </summary>
    bool IsSameFile(string first, string second);

    /// <summary>
    /// Free bytes on the volume holding <paramref name="directory"/>, or <see langword="null"/> when unknown.
    /// </summary>
    long? AvailableFreeSpace(string directory);

    bool IsWritableDirectory(string directory);
}
