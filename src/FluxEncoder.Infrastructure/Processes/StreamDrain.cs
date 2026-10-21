using System.Text;

namespace FluxEncoder.Infrastructure.Processes;

/// <summary>
/// Reads a redirected process stream to its end with bounded memory. Callers start these readers as soon as the
/// process starts and await them after exit, so a full pipe can never block the child.
/// </summary>
internal static class StreamDrain
{
    /// <summary>
    /// Keeps the first <paramref name="maxChars"/> characters and discards the rest while still reading.
    /// </summary>
    public static async Task<(string Text, bool Truncated)> ReadHeadAsync(StreamReader reader, int maxChars)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        var truncated = false;
        int read;

        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var room = maxChars - builder.Length;
            if (room > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, room));
            }

            truncated |= read > room;
        }

        return (builder.ToString(), truncated);
    }

    /// <summary>
    /// Keeps the last <paramref name="maxChars"/> characters, which is where FFmpeg reports the error that ended a run.
    /// </summary>
    public static async Task<string> ReadTailAsync(StreamReader reader, int maxChars)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;

        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > maxChars * 2)
            {
                builder.Remove(0, builder.Length - maxChars);
            }
        }

        if (builder.Length > maxChars)
        {
            builder.Remove(0, builder.Length - maxChars);
        }

        return builder.ToString();
    }
}
