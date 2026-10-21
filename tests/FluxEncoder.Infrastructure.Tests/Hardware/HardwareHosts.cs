namespace FluxEncoder.Infrastructure.Tests.Hardware;

/// <summary>
/// FFmpeg installations used by hardware tests. These depend on the machine, so each test skips when its host is
/// missing.
/// </summary>
internal static class HardwareHosts
{
    /// <summary>
    /// Set to a vendor (<c>amd</c>, <c>nvidia</c>, <c>intel</c>, <c>apple</c>) to assert that its encoders work here.
    /// Without it, hardware tests only assert facts that hold on every machine.
    /// </summary>
    public const string ExpectedVendorVariable = "FLUXENCODER_HARDWARE";

    /// <summary>
    /// A Windows FFmpeg reachable from WSL through interop. Probes write no files, so no path translation is needed.
    /// Override with <c>FLUXENCODER_WINDOWS_FFMPEG</c>.
    /// </summary>
    public static string? WindowsFFmpeg =>
        Environment.GetEnvironmentVariable("FLUXENCODER_WINDOWS_FFMPEG") is { Length: > 0 } configured && File.Exists(configured)
            ? configured
            : Directory.Exists("/mnt/c/Users")
                ? Directory.EnumerateDirectories("/mnt/c/Users")
                    .Select(user => Path.Combine(user, "AppData", "Local", "Microsoft", "WinGet", "Links", "ffmpeg.exe"))
                    .FirstOrDefault(SafeExists)
                : null;

    /// <summary>
    /// Debian's FFmpeg, which lists NVENC and QSV encoders that cannot run without the matching hardware.
    /// </summary>
    public static (string FFmpeg, string FFprobe)? DistributionFFmpeg =>
        File.Exists("/usr/bin/ffmpeg") && File.Exists("/usr/bin/ffprobe") ? ("/usr/bin/ffmpeg", "/usr/bin/ffprobe") : null;

    public static string? ExpectedVendor => Environment.GetEnvironmentVariable(ExpectedVendorVariable)?.Trim().ToLowerInvariant();

    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
