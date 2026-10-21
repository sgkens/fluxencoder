using FluxEncoder.Infrastructure.Secrets;

namespace FluxEncoder.Infrastructure.Tests.Secrets;

/// <summary>
/// The command-line credential stores against stand-in tools that record exactly what they receive, proving the
/// password reaches the tool through standard input and never through its arguments.
/// </summary>
public sealed class SecretStoreTests : IDisposable
{
    private const string Password = "pa ss\"w\\rd$(rm)-123";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-secrets-" + Guid.NewGuid().ToString("N"));

    public SecretStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// A fake tool that appends its arguments to args.log and its stdin to stdin.log, keeps the last stored secret in
    /// store.txt, and prints it for lookups.
    /// </summary>
    private string FakeTool(string name, string lookupWord, string storeWord)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, $$"""
            #!/bin/bash
            dir="$(dirname "$0")"
            printf '%s\n' "$*" >> "$dir/args.log"
            input="$(cat)"
            printf '%s\n' "$input" >> "$dir/stdin.log"
            case "$1" in
              {{storeWord}}) printf '%s' "$input" > "$dir/store.txt" ;;
              {{lookupWord}}) [ -f "$dir/store.txt" ] && cat "$dir/store.txt" || exit 1 ;;
              clear|delete-generic-password) rm -f "$dir/store.txt" ;;
            esac
            """.Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private string Log(string name) => File.ReadAllText(Path.Combine(_directory, name));

    [Fact]
    public async Task SecretTool_StoresThroughStdinAndReadsBack()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses a bash stand-in for secret-tool.");
        var store = new SecretToolStore(FakeTool("secret-tool", "lookup", "store"));

        await store.SetAsync("remote-nas", Password, Token);
        var read = await store.GetAsync("remote-nas", Token);

        Assert.Equal(Password, read);
        Assert.DoesNotContain("rd$(rm)", Log("args.log"), StringComparison.Ordinal);
        Assert.Contains(Password, Log("stdin.log"), StringComparison.Ordinal);
        Assert.Contains("store --label FluxEncoder: remote-nas application fluxencoder key remote-nas", Log("args.log"), StringComparison.Ordinal);

        Assert.True(await store.DeleteAsync("remote-nas", Token));
        Assert.Null(await store.GetAsync("remote-nas", Token));
    }

    [Fact]
    public async Task Keychain_StoresThroughInteractiveStdin()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses a bash stand-in for security.");
        var store = new KeychainStore(FakeTool("security", "find-generic-password", "-i"));

        await store.SetAsync("remote-nas", Password, Token);

        Assert.DoesNotContain("rd$(rm)", Log("args.log"), StringComparison.Ordinal);
        Assert.Equal("-i", Log("args.log").Trim());
        Assert.Contains("""add-generic-password -U -s "FluxEncoder" -a "remote-nas" -w "pa ss\"w\\rd$(rm)-123" """.Trim(), Log("stdin.log"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keychain_RefusesLineBreaks()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses a bash stand-in for security.");
        var store = new KeychainStore(FakeTool("security", "find-generic-password", "-i"));

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("k", "a\nadd-generic-password -s evil", Token));
    }

    [Fact]
    public void MissingTool_IsUnavailableWithHint()
    {
        var store = new SecretToolStore(Path.Combine(_directory, "absent"));

        Assert.False(store.IsAvailable);
        Assert.Contains("FLUXENCODER_SECRET_", store.UnavailableReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("remote-nas", "FLUXENCODER_SECRET_REMOTE_NAS")]
    [InlineData("remote-my.box", "FLUXENCODER_SECRET_REMOTE_MY_BOX")]
    public void Environment_VariableNames(string key, string expected)
    {
        Assert.Equal(expected, EnvironmentSecretStore.VariableName(key));
    }

    [Fact]
    public async Task Read_PrefersEnvironmentThenPlatform()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses a bash stand-in for secret-tool.");
        var platform = new SecretToolStore(FakeTool("secret-tool", "lookup", "store"));
        await platform.SetAsync("remote-nas", "from-store", Token);

        Assert.Equal("from-env", await SecretStores.ReadAsync("remote-nas", n => n == "FLUXENCODER_SECRET_REMOTE_NAS" ? "from-env" : null, platform, Token));
        Assert.Equal("from-store", await SecretStores.ReadAsync("remote-nas", _ => null, platform, Token));
        Assert.Null(await SecretStores.ReadAsync("remote-nas", _ => null, new SecretToolStore(Path.Combine(_directory, "absent")), Token));
    }
}
