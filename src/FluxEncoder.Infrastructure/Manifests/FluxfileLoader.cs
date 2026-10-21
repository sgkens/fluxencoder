using System.Collections.Immutable;
using System.Text;
using FluxEncoder.Core.Manifests;

namespace FluxEncoder.Infrastructure.Manifests;

/// <summary>
/// Finds, reads, and validates a Fluxfile. Reading never runs, opens, or resolves anything the file names; that
/// happens only when the jobs are planned.
/// </summary>
public static class FluxfileLoader
{
    /// <summary>
    /// Real manifests are a few kilobytes; the cap keeps a hostile file from exhausting memory before parsing starts.
    /// </summary>
    public const long MaxBytes = 1024 * 1024;

    /// <summary>
    /// Looked for in this order in the working directory when no file is given.
    /// </summary>
    public static readonly ImmutableArray<string> DefaultNames = ["Fluxfile.yaml", "Fluxfile.yml", "Fluxfile.json"];

    public static string? FindDefault(string directory) =>
        DefaultNames.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);

    public static (Fluxfile? File, ImmutableArray<ManifestError> Errors) Load(FluxfileReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (root, error) = Parse(options.Path);
        return root is null ? (null, [error!]) : FluxfileReader.Read(root, options);
    }

    public static (ManifestNode? Root, ManifestError? Error) Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string text;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return (null, new ManifestError(null, "The file does not exist."));
            }

            if (info.Length > MaxBytes)
            {
                return (null, new ManifestError(null, $"The file is {info.Length / 1024} KB; a Fluxfile may be at most {MaxBytes / 1024} KB."));
            }

            text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return (null, new ManifestError(null, ex is DecoderFallbackException ? "The file is not valid UTF-8 text." : $"The file could not be read: {ex.Message}"));
        }

        return Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ManifestParser.ParseJson(text)
            : ManifestParser.ParseYaml(text);
    }
}
