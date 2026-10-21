namespace FluxEncoder.Infrastructure.Processes;

/// <summary>
/// Captured result of a short-lived process run by <see cref="ProcessCapture"/>.
/// </summary>
/// <param name="ExitCode">The process exit code; meaningless when <paramref name="TimedOut"/> is set.</param>
/// <param name="StandardOutput">Captured stdout, cut at the capture limit.</param>
/// <param name="StandardError">The tail of stderr, which is where FFmpeg puts the error that matters.</param>
/// <param name="TimedOut">The process exceeded its timeout and was killed.</param>
/// <param name="OutputTruncated">Stdout exceeded the capture limit.</param>
public sealed record ProcessOutput(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool OutputTruncated)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
