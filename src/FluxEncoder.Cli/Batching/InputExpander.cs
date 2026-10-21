using FluxEncoder.Core.Media;

namespace FluxEncoder.Cli.Batching;

/// <summary>
/// Turns command-line input arguments into the ordered list of inputs to process.
/// </summary>
/// <remarks>
/// A directory expands to the media files directly inside it (not subdirectories), sorted by name. Everything
/// else, including paths that do not exist and URLs, passes through unchanged so the prober reports the exact
/// problem. Duplicates are removed, keeping the first occurrence.
/// </remarks>
internal static class InputExpander
{
    public static readonly IReadOnlySet<string> MediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".webm", ".ts", ".m2ts", ".mts", ".wmv", ".flv", ".mpg", ".mpeg",
        ".ogv", ".3gp", ".mka", ".m4a", ".mp3", ".flac", ".wav", ".ogg", ".opus", ".aac", ".ac3",
    };

    /// <returns>The inputs, and the directories that held no media files.</returns>
    public static (IReadOnlyList<string> Inputs, IReadOnlyList<string> EmptyDirectories) Expand(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var inputs = new List<string>();
        var empty = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        void Add(string input)
        {
            var key = InputSource.Classify(input).Kind == InputKind.LocalFile ? Path.GetFullPath(input) : input;
            if (seen.Add(key))
            {
                inputs.Add(input);
            }
        }

        foreach (var argument in arguments)
        {
            if (InputSource.Classify(argument).Kind == InputKind.LocalFile && Directory.Exists(argument))
            {
                var files = Directory.EnumerateFiles(argument)
                    .Where(f => MediaExtensions.Contains(Path.GetExtension(f)) && !Path.GetFileName(f).StartsWith('.'))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (files.Count == 0)
                {
                    empty.Add(argument);
                }

                files.ForEach(Add);
            }
            else
            {
                Add(argument);
            }
        }

        return (inputs, empty);
    }
}
