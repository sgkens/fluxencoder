using FluxEncoder.Infrastructure.Processes;

namespace FluxEncoder.Infrastructure.Tests.Processes;

public sealed class ProcessCaptureTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static (string Shell, string Flag) Shell() =>
        OperatingSystem.IsWindows() ? ("cmd.exe", "/c") : ("/bin/sh", "-c");

    private static Task<ProcessOutput> RunScript(string script, TimeSpan timeout, CancellationToken ct = default, int maxOutput = ProcessCapture.DefaultMaxOutputChars, int maxError = ProcessCapture.DefaultMaxErrorChars)
    {
        var (shell, flag) = Shell();
        return ProcessCapture.RunAsync(shell, [flag, script], timeout, ct, maxOutput, maxError);
    }

    [Fact]
    public async Task CapturesStdoutStderrAndExitCode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Script uses POSIX shell syntax.");

        var output = await RunScript("echo out; echo err 1>&2; exit 3", Generous, TestContext.Current.CancellationToken);

        Assert.Equal(3, output.ExitCode);
        Assert.Equal("out", output.StandardOutput.Trim());
        Assert.Equal("err", output.StandardError.Trim());
        Assert.False(output.Succeeded);
        Assert.False(output.TimedOut);
    }

    [Fact]
    public async Task LargeStderr_DoesNotDeadlockAndKeepsTail()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Script uses POSIX shell syntax.");

        // ~1 MB on stderr would fill a 64 KB pipe and hang if stderr were read only after exit.
        var output = await RunScript(
            "i=0; while [ $i -lt 20000 ]; do echo \"line $i of noise padding padding padding\" 1>&2; i=$((i+1)); done; echo done",
            Generous,
            TestContext.Current.CancellationToken,
            maxError: 1000);

        Assert.True(output.Succeeded);
        Assert.Equal("done", output.StandardOutput.Trim());
        Assert.True(output.StandardError.Length <= 1000);
        Assert.Contains("line 19999", output.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutputBeyondLimit_IsTruncatedButDrained()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Script uses POSIX shell syntax.");

        var output = await RunScript("i=0; while [ $i -lt 5000 ]; do echo 0123456789; i=$((i+1)); done", Generous, TestContext.Current.CancellationToken, maxOutput: 100);

        Assert.True(output.Succeeded);
        Assert.True(output.OutputTruncated);
        Assert.Equal(100, output.StandardOutput.Length);
    }

    [Fact]
    public async Task Timeout_KillsAndReports()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Script uses POSIX shell syntax.");

        var started = DateTime.UtcNow;
        var output = await RunScript("sleep 30", TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        Assert.True(output.TimedOut);
        Assert.False(output.Succeeded);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Cancellation_KillsAndThrows()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Script uses POSIX shell syntax.");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunScript("sleep 30", Generous, cts.Token));
    }

    [Fact]
    public async Task ArgumentsWithSpacesAndQuotes_ArriveIntact()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Script uses POSIX shell syntax.");

        const string Tricky = "My \"Movie\" (2024) $HOME;.mkv";
        var output = await ProcessCapture.RunAsync("/bin/sh", ["-c", "printf '%s' \"$1\"", "sh", Tricky], Generous, TestContext.Current.CancellationToken);

        Assert.Equal(Tricky, output.StandardOutput);
    }

    [Fact]
    public async Task MissingExecutable_Throws()
    {
        await Assert.ThrowsAnyAsync<System.ComponentModel.Win32Exception>(
            () => ProcessCapture.RunAsync(Path.Combine(Path.GetTempPath(), "no-such-tool-" + Guid.NewGuid().ToString("N")), [], Generous, TestContext.Current.CancellationToken));
    }
}
