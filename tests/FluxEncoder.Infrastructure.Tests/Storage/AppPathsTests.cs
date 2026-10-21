using FluxEncoder.Infrastructure.Storage;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Tests.Storage;

public sealed class AppPathsTests
{
    private const string Home = "/home/user";

    private static AppPaths Resolve(HostPlatform platform, params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return AppPaths.Resolve(name => map.GetValueOrDefault(name), platform, Home);
    }

    [Fact]
    public void Linux_FollowsXdgDefaults()
    {
        var paths = Resolve(HostPlatform.Linux);

        Assert.Equal(Path.Combine(Home, ".config", "fluxencoder"), paths.Config);
        Assert.Equal(Path.Combine(Home, ".local", "share", "fluxencoder"), paths.Data);
        Assert.Equal(Path.Combine(Home, ".cache", "fluxencoder"), paths.Cache);
        Assert.Equal(Path.Combine(Home, ".config", "fluxencoder", "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(Home, ".local", "share", "fluxencoder", "history.db"), paths.HistoryDatabase);
    }

    [Fact]
    public void Linux_HonoursAbsoluteXdgVariablesOnly()
    {
        var paths = Resolve(HostPlatform.Linux, ("XDG_CONFIG_HOME", "/cfg"), ("XDG_DATA_HOME", "relative/ignored"));

        Assert.Equal(Path.Combine("/cfg", "fluxencoder"), paths.Config);
        Assert.Equal(Path.Combine(Home, ".local", "share", "fluxencoder"), paths.Data);
    }

    [Fact]
    public void MacOS_UsesLibraryFolders()
    {
        var paths = Resolve(HostPlatform.MacOS);

        Assert.Equal(Path.Combine(Home, "Library", "Application Support", "FluxEncoder"), paths.Config);
        Assert.Equal(paths.Config, paths.Data);
        Assert.Equal(Path.Combine(Home, "Library", "Caches", "FluxEncoder"), paths.Cache);
    }

    [Fact]
    public void Windows_KeepsDatabaseOutOfRoamingProfile()
    {
        var paths = Resolve(HostPlatform.Windows, ("APPDATA", "/roaming"), ("LOCALAPPDATA", "/local"));

        Assert.Equal(Path.Combine("/roaming", "FluxEncoder"), paths.Config);
        Assert.Equal(Path.Combine("/local", "FluxEncoder"), paths.Data);
        Assert.Equal(Path.Combine("/local", "FluxEncoder", "Cache"), paths.Cache);
    }

    [Fact]
    public void HomeOverride_GroupsEverythingUnderOneFolder()
    {
        var paths = Resolve(HostPlatform.Windows, (AppPaths.HomeVariable, "/portable"), ("APPDATA", "/roaming"));

        Assert.Equal(new AppPaths(Path.Combine("/portable", "config"), Path.Combine("/portable", "data"), Path.Combine("/portable", "cache")), paths);
    }

    [Fact]
    public void EnsureDirectory_IsOwnerOnlyOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix permissions.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "flux-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppPaths.EnsureDirectory(Path.Combine(root, "a"));

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(root, "a")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
