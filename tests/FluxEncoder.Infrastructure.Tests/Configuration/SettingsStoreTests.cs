using System.Collections.Immutable;
using FluxEncoder.Infrastructure.Configuration;

namespace FluxEncoder.Infrastructure.Tests.Configuration;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string File(string name = "settings.json") => Path.Combine(_directory, name);

    private (FluxSettings Settings, IReadOnlyList<string> Warnings) LoadText(string json)
    {
        System.IO.File.WriteAllText(File(), json);
        return SettingsStore.Load(File());
    }

    [Fact]
    public void MissingFile_IsDefaultsWithoutWarnings()
    {
        var (settings, warnings) = SettingsStore.Load(File("absent.json"));

        Assert.Equal(FluxSettings.Default, settings);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Template_RoundTrips()
    {
        SettingsStore.Save(File(), SettingsStore.Template);

        var (settings, warnings) = SettingsStore.Load(File());

        Assert.Empty(warnings);
        Assert.Equal(SettingsStore.Template, settings);
        Assert.Equal("hevc", settings.Presets["ARCHIVE"].Options["codec"]);
    }

    [Fact]
    public void Save_IsOwnerOnlyAndKeepsBackupOfPreviousVersion()
    {
        SettingsStore.Save(File(), FluxSettings.Default);
        SettingsStore.Save(File(), SettingsStore.Template);

        Assert.True(System.IO.File.Exists(File("settings.json.bak")));
        Assert.Equal(FluxSettings.Default, SettingsStore.Load(File("settings.json.bak")).Settings);
        Assert.Empty(Directory.GetFiles(_directory, ".*.tmp"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, System.IO.File.GetUnixFileMode(File()));
        }
    }

    [Fact]
    public void Values_AcceptStringsNumbersAndBooleans()
    {
        var (settings, warnings) = LoadText("""
            { "encode": { "codec": "hevc", "retries": 2, "fail-fast": true, "--quality": "high" } }
            """);

        Assert.Empty(warnings);
        Assert.Equal("hevc", settings.Encode["codec"]);
        Assert.Equal("2", settings.Encode["retries"]);
        Assert.Equal("true", settings.Encode["fail-fast"]);
        Assert.Equal("high", settings.Encode["quality"]);
    }

    [Fact]
    public void InvalidJson_WarnsWithPositionAndLeavesFileUntouched()
    {
        const string Broken = "{ \"encode\": { \"codec\": \"hevc\" ";

        var (settings, warnings) = LoadText(Broken);

        Assert.Equal(FluxSettings.Default, settings);
        var warning = Assert.Single(warnings);
        Assert.Contains("not valid JSON", warning, StringComparison.Ordinal);
        Assert.Contains("config init --force", warning, StringComparison.Ordinal);
        Assert.Equal(Broken, System.IO.File.ReadAllText(File()));
    }

    [Theory]
    [InlineData("[]", "must contain a JSON object")]
    [InlineData("""{ "colour": "blue" }""", "unknown setting 'colour'")]
    [InlineData("""{ "encode": [] }""", "must be an object")]
    [InlineData("""{ "encode": { "codec": ["hevc"] } }""", "encode.codec")]
    [InlineData("""{ "presets": { "x": "hevc" } }""", "preset 'x' must be an object")]
    [InlineData("""{ "history": { "retentionDays": 0 } }""", "history.retentionDays")]
    [InlineData("""{ "history": { "enabled": "yes" } }""", "history.enabled")]
    [InlineData("""{ "history": { "maxRuns": "lots" } }""", "history.maxRuns")]
    [InlineData("""{ "history": { "colour": 1 } }""", "history.colour")]
    [InlineData("""{ "schemaVersion": 9 }""", "newer FluxEncoder")]
    public void WrongShapes_WarnSpecifically(string json, string expected)
    {
        var (_, warnings) = LoadText(json);

        Assert.Contains(warnings, w => w.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void BadHistoryValue_FallsBackOnlyForThatValue()
    {
        var (settings, _) = LoadText("""{ "history": { "retentionDays": -5, "maxRuns": 50, "telemetry": false } }""");

        Assert.Equal(new HistorySettings(Enabled: true, Telemetry: false, RetentionDays: 90, MaxRuns: 50), settings.History);
    }

    [Fact]
    public void Comments_AndTrailingCommasAreAccepted()
    {
        var (settings, warnings) = LoadText("""
            {
              // my defaults
              "encode": { "codec": "av1", },
            }
            """);

        Assert.Empty(warnings);
        Assert.Equal("av1", settings.Encode["codec"]);
    }

    [Fact]
    public void Presets_KeepDescriptionsSeparateFromOptions()
    {
        var (settings, _) = LoadText("""{ "presets": { "Tiny": { "description": "small", "codec": "av1" } } }""");

        var preset = settings.Presets["tiny"];
        Assert.Equal("small", preset.Description);
        Assert.Equal(ImmutableDictionary<string, string>.Empty.Add("codec", "av1"), preset.Options);
    }

    [Fact]
    public void SecurityAndLimits_ParseAndRoundTrip()
    {
        var root = Path.GetFullPath(Path.Combine(_directory, "media"));
        var (settings, warnings) = LoadText($$"""
            {
              "security": { "allowExtraArgs": false, "allowPipes": false, "allowedSchemes": ["https", "SRT"], "allowedRoots": ["{{root.Replace("\\", "\\\\")}}"] },
              "limits": { "maxRunMinutes": 90, "maxInputMB": 4096, "maxOutputMB": 2048, "threads": 4 }
            }
            """);

        Assert.Empty(warnings);
        Assert.False(settings.Security.AllowExtraArguments);
        Assert.False(settings.Security.AllowPipes);
        Assert.True(settings.Security.AllowDevices);
        Assert.Equal(["https", "srt"], settings.Security.AllowedSchemes.Order(StringComparer.Ordinal));
        Assert.Equal([root], settings.Security.AllowedRoots);
        Assert.Equal(TimeSpan.FromMinutes(90), settings.Limits.MaxRunTime);
        Assert.Equal(4096L * 1024 * 1024, settings.Limits.MaxInputBytes);
        Assert.Equal(2048L * 1024 * 1024, settings.Limits.MaxOutputBytes);
        Assert.Equal(4, settings.Limits.Threads);

        SettingsStore.Save(File(), settings);
        Assert.Equal(settings, SettingsStore.Load(File()).Settings);
    }

    [Theory]
    [InlineData("""{ "security": { "allowedSchemes": ["gopher"] } }""", "'gopher' is not a protocol")]
    [InlineData("""{ "security": { "allowedRoots": ["relative/dir"] } }""", "not an absolute path")]
    [InlineData("""{ "security": { "allowPipes": "no" } }""", "security.allowPipes")]
    [InlineData("""{ "security": { "colour": 1 } }""", "security.colour")]
    [InlineData("""{ "limits": { "threads": 1000 } }""", "limits.threads")]
    [InlineData("""{ "limits": { "maxRunMinutes": "soon" } }""", "limits.maxRunMinutes")]
    [InlineData("""{ "limits": { "speed": 1 } }""", "limits.speed")]
    public void SecurityAndLimits_WarnSpecifically(string json, string expected)
    {
        var (_, warnings) = LoadText(json);

        Assert.Contains(warnings, w => w.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Defaults_AreUnrestricted()
    {
        var (settings, _) = LoadText("{}");

        Assert.Equal(Core.Security.SourcePolicy.Default, settings.Security);
        Assert.Equal(Core.Security.ResourceLimits.None, settings.Limits);
    }
}
