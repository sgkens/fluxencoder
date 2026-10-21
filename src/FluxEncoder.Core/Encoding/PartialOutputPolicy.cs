namespace FluxEncoder.Core.Encoding;

/// <summary>
/// What to do with the temporary output of a job that failed or was cancelled.
/// </summary>
/// <remarks>
/// FFmpeg always writes to a temporary path beside the destination (<see cref="OutputPaths.Temporary"/>);
/// only a validated result is moved to the real name, so a failed job never leaves a truncated file under
/// the name the user asked for.
/// </remarks>
public enum PartialOutputPolicy
{
    Delete,

    /// <summary>
    /// Keep the temporary file under its temporary name, for diagnosing a failed encode.
    /// </summary>
    Keep,
}
