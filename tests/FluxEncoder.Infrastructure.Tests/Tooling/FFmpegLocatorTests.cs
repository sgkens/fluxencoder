using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Tooling;

namespace FluxEncoder.Infrastructure.Tests.Tooling;

public sealed class FFmpegLocatorTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "flux-locator");
    private static readonly string PathDir = Path.Combine(Root, "path-bin");
    private static readonly string OtherDir = Path.Combine(Root, "other-bin");
    private static readonly string AppDir = Path.Combine(Root, "app");

    private static ToolEnvironment Fake(
        IEnumerable<string> executables,
        IDictionary<string, string>? variables = null,
        HostPlatform platform = HostPlatform.Linux,
        string? appDirectory = null)
    {
        var files = executables.ToHashSet(StringComparer.Ordinal);
        var dirs = files.Select(f => Path.GetDirectoryName(f)!).ToHashSet(StringComparer.Ordinal);
        var vars = new Dictionary<string, string>(variables ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        vars.TryAdd("PATH", string.Join(platform == HostPlatform.Windows ? ';' : ':', [PathDir, OtherDir]));

        return new ToolEnvironment
        {
            Platform = platform,
            GetVariable = name => vars.GetValueOrDefault(name),
            IsExecutableFile = files.Contains,
            DirectoryExists = dirs.Contains,
            ApplicationDirectory = appDirectory ?? AppDir,
        };
    }

    private static string In(string dir, string name) => Path.Combine(dir, name);

    [Fact]
    public void FindsBothOnPath()
    {
        var env = Fake([In(PathDir, "ffmpeg"), In(PathDir, "ffprobe")]);

        var result = FFmpegLocator.Locate(ToolPathOverrides.None, env);

        Assert.True(result.IsComplete);
        Assert.Equal(new ToolLocation(FFmpegTool.FFmpeg, In(PathDir, "ffmpeg"), ToolSource.SearchPath), result.FFmpeg);
        Assert.Equal(ToolSource.Sibling, result.FFprobe!.Source);
        Assert.Null(result.ToFailure());
    }

    [Fact]
    public void PrefersFFprobeBesideFFmpegOverEarlierPathEntry()
    {
        var env = Fake([In(PathDir, "ffprobe"), In(OtherDir, "ffmpeg"), In(OtherDir, "ffprobe")]);

        var result = FFmpegLocator.Locate(ToolPathOverrides.None, env);

        Assert.Equal(In(OtherDir, "ffmpeg"), result.FFmpeg!.Path);
        Assert.Equal(In(OtherDir, "ffprobe"), result.FFprobe!.Path);
    }

    [Fact]
    public void ApplicationDirectory_WinsOverPath()
    {
        var env = Fake([In(AppDir, "ffmpeg"), In(AppDir, "ffprobe"), In(PathDir, "ffmpeg"), In(PathDir, "ffprobe")]);

        var result = FFmpegLocator.Locate(ToolPathOverrides.None, env);

        Assert.Equal(ToolSource.ApplicationDirectory, result.FFmpeg!.Source);
        Assert.Equal(In(AppDir, "ffprobe"), result.FFprobe!.Path);
    }

    [Fact]
    public void BundledSubdirectory_WinsOverPath()
    {
        var bundled = Path.Combine(AppDir, "ffmpeg");
        var env = Fake([In(bundled, "ffmpeg"), In(bundled, "ffprobe"), In(PathDir, "ffmpeg"), In(PathDir, "ffprobe")]);

        var result = FFmpegLocator.Locate(ToolPathOverrides.None, env);

        Assert.Equal(new ToolLocation(FFmpegTool.FFmpeg, In(bundled, "ffmpeg"), ToolSource.ApplicationDirectory), result.FFmpeg);
        Assert.Equal(In(bundled, "ffprobe"), result.FFprobe!.Path);
    }

    [Fact]
    public void CustomOverride_WinsOverBundledSubdirectory()
    {
        var bundled = Path.Combine(AppDir, "ffmpeg");
        var custom = Path.Combine(Root, "custom-ffmpeg-7.1");
        var env = Fake(
            [In(bundled, "ffmpeg"), In(bundled, "ffprobe"), In(custom, "ffmpeg"), In(custom, "ffprobe")],
            new Dictionary<string, string> { [FFmpegLocator.FFmpegVariable] = custom });

        var result = FFmpegLocator.Locate(ToolPathOverrides.None, env);

        Assert.Equal(new ToolLocation(FFmpegTool.FFmpeg, In(custom, "ffmpeg"), ToolSource.EnvironmentVariable), result.FFmpeg);
        Assert.Equal(In(custom, "ffprobe"), result.FFprobe!.Path);
    }

    [Fact]
    public void ConfiguredDirectory_ResolvesBothTools()
    {
        var custom = Path.Combine(Root, "custom");
        var env = Fake([In(custom, "ffmpeg"), In(custom, "ffprobe"), In(PathDir, "ffmpeg"), In(PathDir, "ffprobe")]);

        var result = FFmpegLocator.Locate(new ToolPathOverrides(FFmpeg: custom), env);

        Assert.Equal(new ToolLocation(FFmpegTool.FFmpeg, In(custom, "ffmpeg"), ToolSource.Configured), result.FFmpeg);
        Assert.Equal(new ToolLocation(FFmpegTool.FFprobe, In(custom, "ffprobe"), ToolSource.Sibling), result.FFprobe);
    }

    [Fact]
    public void ConfiguredFFprobeOnly_AnchorsFFmpegBesideIt()
    {
        var custom = Path.Combine(Root, "custom");
        var env = Fake([In(custom, "ffmpeg"), In(custom, "ffprobe"), In(PathDir, "ffmpeg"), In(PathDir, "ffprobe")]);

        var result = FFmpegLocator.Locate(new ToolPathOverrides(FFprobe: In(custom, "ffprobe")), env);

        Assert.Equal(In(custom, "ffmpeg"), result.FFmpeg!.Path);
        Assert.Equal(ToolSource.Sibling, result.FFmpeg.Source);
    }

    [Fact]
    public void InvalidOverride_DoesNotFallBackToPath()
    {
        var env = Fake([In(PathDir, "ffmpeg"), In(PathDir, "ffprobe")]);
        var typo = Path.Combine(Root, "typo", "ffmpeg");

        var result = FFmpegLocator.Locate(new ToolPathOverrides(FFmpeg: typo), env);

        Assert.Null(result.FFmpeg);
        Assert.Contains(result.Problems, p => p.Contains(typo, StringComparison.Ordinal));

        var failure = result.ToFailure()!;
        Assert.Equal(FailureCategory.DependencyMissing, failure.Category);
        Assert.Equal("FFmpeg was not found.", failure.Message);
        Assert.Contains(typo, failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentVariable_IsUsedWhenNoOverride()
    {
        var custom = Path.Combine(Root, "env");
        var env = Fake(
            [In(custom, "ffmpeg"), In(custom, "ffprobe"), In(PathDir, "ffmpeg")],
            new Dictionary<string, string> { [FFmpegLocator.FFmpegVariable] = In(custom, "ffmpeg") });

        var result = FFmpegLocator.Locate(ToolPathOverrides.None, env);

        Assert.Equal(ToolSource.EnvironmentVariable, result.FFmpeg!.Source);
        Assert.Equal(In(custom, "ffprobe"), result.FFprobe!.Path);
    }

    [Fact]
    public void Override_TakesPrecedenceOverEnvironmentVariable()
    {
        var fromEnv = Path.Combine(Root, "env");
        var configured = Path.Combine(Root, "cfg");
        var env = Fake(
            [In(fromEnv, "ffmpeg"), In(configured, "ffmpeg"), In(configured, "ffprobe")],
            new Dictionary<string, string> { [FFmpegLocator.FFmpegVariable] = fromEnv });

        var result = FFmpegLocator.Locate(new ToolPathOverrides(FFmpeg: configured), env);

        Assert.Equal(In(configured, "ffmpeg"), result.FFmpeg!.Path);
    }

    [Fact]
    public void Windows_UsesExeNamesAndWellKnownLocations()
    {
        var winget = Path.Combine(Root, "Local", "Microsoft", "WinGet", "Links");
        var env = Fake(
            [In(winget, "ffmpeg.exe"), In(winget, "ffprobe.exe")],
            new Dictionary<string, string> { ["LOCALAPPDATA"] = Path.Combine(Root, "Local") },
            HostPlatform.Windows);

        var result = FFmpegLocator.Locate(ToolPathOverrides.None, env);

        Assert.Equal(new ToolLocation(FFmpegTool.FFmpeg, In(winget, "ffmpeg.exe"), ToolSource.WellKnownLocation), result.FFmpeg);
        Assert.Equal(In(winget, "ffprobe.exe"), result.FFprobe!.Path);
    }

    [Fact]
    public void NothingFound_ListsSearchedLocationsAndInstallHint()
    {
        var result = FFmpegLocator.Locate(ToolPathOverrides.None, Fake([], platform: HostPlatform.MacOS));

        var failure = result.ToFailure()!;
        Assert.Equal("FFmpeg and FFprobe were not found.", failure.Message);
        Assert.Contains(In(PathDir, "ffmpeg"), failure.Detail, StringComparison.Ordinal);
        Assert.Contains("/opt/homebrew/bin", failure.Detail, StringComparison.Ordinal);
        Assert.Contains("brew install ffmpeg", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void FFprobeMissing_IsReportedAlone()
    {
        var result = FFmpegLocator.Locate(ToolPathOverrides.None, Fake([In(PathDir, "ffmpeg")]));

        Assert.NotNull(result.FFmpeg);
        Assert.Equal("FFprobe was not found.", result.ToFailure()!.Message);
    }

    [Fact]
    public void QuotedPathEntries_AreUnquoted()
    {
        var env = Fake(
            [In(PathDir, "ffmpeg"), In(PathDir, "ffprobe")],
            new Dictionary<string, string> { ["PATH"] = $"\"{PathDir}\"" });

        Assert.True(FFmpegLocator.Locate(ToolPathOverrides.None, env).IsComplete);
    }

    [Fact]
    public void RealHost_FindsInstalledTools()
    {
        var result = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);
        Assert.SkipUnless(result.IsComplete, "FFmpeg is not installed on this machine.");

        Assert.True(File.Exists(result.FFmpeg!.Path));
        Assert.True(File.Exists(result.FFprobe!.Path));
    }
}
