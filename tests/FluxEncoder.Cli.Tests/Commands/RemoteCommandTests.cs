using System.Text.Json;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Infrastructure.Secrets;
using FluxEncoder.Infrastructure.Storage;
using FluxEncoder.Infrastructure.Tooling;
using Microsoft.Data.Sqlite;

namespace FluxEncoder.Cli.Tests.Commands;

/// <summary>
/// <c>remote</c> and <c>remote://</c> inputs end to end, with an in-memory credential store and a temporary
/// application folder.
/// </summary>
public sealed class RemoteCommandTests : IDisposable
{
    private const string Password = "N4s-p@ss:w0rd#1";

    private static readonly ToolDiscovery Tools = FFmpegLocator.Locate(ToolPathOverrides.None, ToolEnvironment.Current);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "flux-remote-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryStore _store = new();

    public RemoteCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private AppPaths Paths => new(Path.Combine(_directory, "config"), Path.Combine(_directory, "data"), Path.Combine(_directory, "cache"));

    private sealed record Run(int ExitCode, string Out, string Error);

    private async Task<Run> RunWithAsync(string stdin, IReadOnlyDictionary<string, string>? variables, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(output, error, name => variables?.GetValueOrDefault(name), ErrorIsTerminal: false)
        {
            Paths = Paths,
            In = new StringReader(stdin),
            SecretStore = () => _store,
        };

        var exitCode = await CliApplication.RunAsync(args, host, TestContext.Current.CancellationToken);
        return new Run(exitCode, output.ToString(), error.ToString());
    }

    private Task<Run> RunAsync(params string[] args) => RunWithAsync(string.Empty, null, args);

    private sealed class MemoryStore : ISecretStore
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public string Name => "test store";

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Secrets.GetValueOrDefault(key));

        public Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
        {
            Secrets[key] = secret;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Secrets.Remove(key));
    }

    [Fact]
    public async Task Add_StoresLocationInSettingsAndPasswordOnlyInStore()
    {
        var run = await RunWithAsync(Password + "\n", null, "remote", "add", "nas", "ftp://jane@127.0.0.1:9/media", "--password-stdin");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(Password, _store.Secrets["remote-nas"]);
        var settings = await File.ReadAllTextAsync(Paths.SettingsFile, TestContext.Current.CancellationToken);
        Assert.Contains("ftp://jane@127.0.0.1:9/media", settings, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_RefusesPasswordInUrl()
    {
        var run = await RunAsync("remote", "add", "nas", "ftp://jane:hunter22@host/media");

        Assert.Equal(2, run.ExitCode);
        Assert.DoesNotContain("hunter22", run.Out + run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Paths.SettingsFile));
    }

    [Fact]
    public async Task List_ShowsWhereThePasswordComesFromButNotThePassword()
    {
        await RunWithAsync(Password, null, "remote", "add", "nas", "ftp://jane@host/media", "--password-stdin");
        await RunAsync("remote", "add", "pub", "ftp://files.example/");

        var run = await RunWithAsync(string.Empty, new Dictionary<string, string> { ["FLUXENCODER_SECRET_REMOTE_PUB"] = "envpw" }, "remote", "list");

        Assert.Contains("nas  ftp://jane@host/media  (password in test store)", run.Out, StringComparison.Ordinal);
        Assert.Contains("pub  ftp://files.example  (password from environment)", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("envpw", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_DeletesLocationAndPassword()
    {
        await RunWithAsync(Password, null, "remote", "add", "nas", "ftp://jane@host/media", "--password-stdin");

        var run = await RunAsync("remote", "remove", "nas");

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(_store.Secrets);
        Assert.Contains("No remotes", (await RunAsync("remote", "list")).Error, StringComparison.Ordinal);
        Assert.Equal(2, (await RunAsync("remote", "remove", "nas")).ExitCode);
    }

    [Fact]
    public async Task EncodeRemote_ResolvesWithPasswordThatAppearsNowhere()
    {
        Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");
        await RunWithAsync(Password, null, "remote", "add", "nas", "ftp://jane@127.0.0.1:9/media", "--password-stdin");
        var output = Path.Combine(_directory, "out.mkv");

        var human = await RunWithAsync(string.Empty, null, "encode", "remote://nas/My Films/a#1.mkv", "-o", output, "--verbose");
        var json = await RunWithAsync(string.Empty, null, "encode", "remote://nas/My Films/a#1.mkv", "-o", output, "--json");

        Assert.Equal(3, human.ExitCode);
        Assert.Contains("ftp://jane:***@127.0.0.1:9/media/My%20Films/a%231.mkv", human.Error, StringComparison.Ordinal);
        foreach (var text in new[] { human.Out, human.Error, json.Out, json.Error })
        {
            Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Uri.EscapeDataString(Password), text, StringComparison.Ordinal);
        }

        using var document = JsonDocument.Parse(json.Out);
        Assert.Equal("network-error", document.RootElement.GetProperty("jobs")[0].GetProperty("failure").GetProperty("category").GetString());

        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Paths.Data))
        {
            var bytes = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
            Assert.DoesNotContain(Password, bytes, StringComparison.Ordinal);
            Assert.DoesNotContain(Uri.EscapeDataString(Password), bytes, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EncodeRemote_UnknownAliasOrEscapingPathFails()
    {
        Assert.SkipUnless(Tools.IsComplete, "FFmpeg is not installed on this machine.");
        await RunAsync("remote", "add", "nas", "ftp://host/media");

        Assert.Equal(2, (await RunAsync("encode", "remote://nope/a.mkv", "-o", Path.Combine(_directory, "x.mkv"))).ExitCode);
        var escaping = await RunAsync("encode", "remote://nas/../secret.mkv", "-o", Path.Combine(_directory, "y.mkv"));
        Assert.Equal(2, escaping.ExitCode);
        Assert.Contains("'..'", escaping.Error, StringComparison.Ordinal);
    }
}
