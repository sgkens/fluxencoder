namespace FluxEncoder.Core.Media;

/// <summary>
/// An input string classified by how it can be read. Classification is lexical only; existence and
/// reachability are checked when probing.
/// </summary>
public sealed record InputSource
{
    private InputSource(string value, InputKind kind, string? scheme)
    {
        Value = value;
        Kind = kind;
        Scheme = scheme;
    }

    /// <summary>
    /// The input as given, passed to FFmpeg unchanged.
    /// </summary>
    public string Value { get; }

    public InputKind Kind { get; }

    /// <summary>
    /// The lower-case URL scheme (<c>https</c>, <c>rtmp</c>), or <see langword="null"/> for plain paths.
    /// </summary>
    public string? Scheme { get; }

    /// <summary>
    /// Only local files allow seeking, which two-pass encoding, accurate trims, and first-frame HDR probes rely on.
    /// </summary>
    public bool IsSeekable => Kind == InputKind.LocalFile;

    /// <summary>
    /// Whether FFprobe can inspect the input without consuming data the encode needs.
    /// </summary>
    public bool CanProbe => Kind != InputKind.Pipe;

    /// <summary>
    /// The file-system path for a local file, with any <c>file:</c> prefix removed.
    /// </summary>
    public string? LocalPath => Kind != InputKind.LocalFile
        ? null
        : Scheme == "file" ? StripFileScheme(Value) : Value;

    public static InputSource Classify(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        if (input is "-" || input.StartsWith("pipe:", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("fd:", StringComparison.OrdinalIgnoreCase)
            || input is "/dev/stdin")
        {
            return new(input, InputKind.Pipe, null);
        }

        var separator = input.IndexOf("://", StringComparison.Ordinal);
        if (separator > 1 && IsScheme(input.AsSpan(0, separator)))
        {
            var scheme = input[..separator].ToLowerInvariant();
            return new(input, scheme == "file" ? InputKind.LocalFile : InputKind.Network, scheme);
        }

        if (input.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return new(input, InputKind.LocalFile, "file");
        }

        if (input.StartsWith("/dev/video", StringComparison.Ordinal) || input.StartsWith("/dev/snd/", StringComparison.Ordinal))
        {
            return new(input, InputKind.Device, null);
        }

        return new(input, InputKind.LocalFile, null);
    }

    // Schemes are at least two characters, so a Windows drive letter (C:) is never read as a protocol.
    private static bool IsScheme(ReadOnlySpan<char> text) =>
        char.IsAsciiLetter(text[0])
        && text.Length > 1
        && !text.ContainsAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+-.");

    private static string StripFileScheme(string value)
    {
        var rest = value["file:".Length..];
        return rest.StartsWith("//", StringComparison.Ordinal) ? Uri.UnescapeDataString(new Uri(value).LocalPath) : rest;
    }
}
