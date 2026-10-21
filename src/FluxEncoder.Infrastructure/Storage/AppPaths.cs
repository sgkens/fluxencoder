using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Storage;

/// <summary>
/// The one place FluxEncoder keeps its files (TSKE009.1), following each platform's convention.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Kind</term><description>Linux (XDG) / macOS / Windows</description></listheader>
/// <item><term>Config</term><description><c>$XDG_CONFIG_HOME/fluxencoder</c> (~/.config) /
/// <c>~/Library/Application Support/FluxEncoder</c> / <c>%APPDATA%\FluxEncoder</c> (roams with the profile)</description></item>
/// <item><term>Data</term><description><c>$XDG_DATA_HOME/fluxencoder</c> (~/.local/share) /
/// <c>~/Library/Application Support/FluxEncoder</c> / <c>%LOCALAPPDATA%\FluxEncoder</c> (never roams: SQLite must not
/// be synced while open)</description></item>
/// <item><term>Cache</term><description><c>$XDG_CACHE_HOME/fluxencoder</c> (~/.cache) /
/// <c>~/Library/Caches/FluxEncoder</c> / <c>%LOCALAPPDATA%\FluxEncoder\Cache</c></description></item>
/// </list>
/// <para>
/// <c>FLUXENCODER_HOME</c> overrides all three with <c>config</c>, <c>data</c>, and <c>cache</c> subdirectories of one
/// folder, for portable installs and tests.
/// </para>
/// <para>
/// Ownership: <see cref="SettingsFile"/> belongs to the user (FluxEncoder writes it only on request, atomically);
/// <see cref="HistoryDatabase"/> and everything under <see cref="Cache"/> belong to FluxEncoder and may be deleted at
/// any time. Directories are created owner-only (0700) on Unix; Windows profile folders are already per-user.
/// </para>
/// </remarks>
public sealed record AppPaths(string Config, string Data, string Cache)
{
    public const string HomeVariable = "FLUXENCODER_HOME";

    private const string LinuxName = "fluxencoder";
    private const string ProductName = "FluxEncoder";

    public string SettingsFile => Path.Combine(Config, "settings.json");

    public string HistoryDatabase => Path.Combine(Data, "history.db");

    public static AppPaths Current { get; } = Resolve(Environment.GetEnvironmentVariable, Platform(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static AppPaths Resolve(Func<string, string?> getVariable, HostPlatform platform, string home)
    {
        ArgumentNullException.ThrowIfNull(getVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);

        if (getVariable(HomeVariable) is { Length: > 0 } root)
        {
            return new AppPaths(Path.Combine(root, "config"), Path.Combine(root, "data"), Path.Combine(root, "cache"));
        }

        string Variable(string name, params string[] fallback) =>
            getVariable(name) is { Length: > 0 } value && Path.IsPathRooted(value) ? value : Path.Combine([home, .. fallback]);

        return platform switch
        {
            HostPlatform.Windows => new AppPaths(
                Path.Combine(Variable("APPDATA", "AppData", "Roaming"), ProductName),
                Path.Combine(Variable("LOCALAPPDATA", "AppData", "Local"), ProductName),
                Path.Combine(Variable("LOCALAPPDATA", "AppData", "Local"), ProductName, "Cache")),
            HostPlatform.MacOS => new AppPaths(
                Path.Combine(home, "Library", "Application Support", ProductName),
                Path.Combine(home, "Library", "Application Support", ProductName),
                Path.Combine(home, "Library", "Caches", ProductName)),
            _ => new AppPaths(
                Path.Combine(Variable("XDG_CONFIG_HOME", ".config"), LinuxName),
                Path.Combine(Variable("XDG_DATA_HOME", ".local", "share"), LinuxName),
                Path.Combine(Variable("XDG_CACHE_HOME", ".cache"), LinuxName)),
        };
    }

    /// <summary>
    /// Creates <paramref name="directory"/> if needed, owner-only on Unix.
    /// </summary>
    public static void EnsureDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static HostPlatform Platform() => ToolEnvironment.Current.Platform;
}
