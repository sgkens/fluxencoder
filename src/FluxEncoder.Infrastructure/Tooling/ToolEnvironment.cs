namespace FluxEncoder.Infrastructure.Tooling;

/// <summary>
/// The parts of the host that tool discovery reads, injectable so discovery is testable without real files.
/// </summary>
public sealed record ToolEnvironment
{
    public required HostPlatform Platform { get; init; }

    public required Func<string, string?> GetVariable { get; init; }

    /// <summary>
    /// Whether a path is an existing file this user may execute.
    /// </summary>
    public required Func<string, bool> IsExecutableFile { get; init; }

    public required Func<string, bool> DirectoryExists { get; init; }

    public string? ApplicationDirectory { get; init; }

    public static ToolEnvironment Current { get; } = new()
    {
        Platform = OperatingSystem.IsWindows() ? HostPlatform.Windows
            : OperatingSystem.IsMacOS() ? HostPlatform.MacOS
            : HostPlatform.Linux,
        GetVariable = Environment.GetEnvironmentVariable,
        IsExecutableFile = IsExecutable,
        DirectoryExists = Directory.Exists,
        ApplicationDirectory = AppContext.BaseDirectory,
    };

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        const UnixFileMode AnyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & AnyExecute) != 0;
    }
}
