namespace FluxEncoder.Core.Results;

/// <summary>
/// Something the user should know about a job that did not stop it, such as a dropped subtitle stream.
/// </summary>
/// <param name="Code">
/// Stable, machine-readable kebab-case identifier (for example <c>subtitle-dropped</c>) for JSON consumers.
/// Treat codes like exit codes: do not rename them once released.
/// </param>
/// <param name="Message">User-facing explanation.</param>
public sealed record JobWarning(string Code, string Message)
{
    public string Code { get; } = IsKebabCase(Code)
        ? Code
        : throw new ArgumentException($"Warning code '{Code}' must be lower-case kebab-case.", nameof(Code));

    public string Message { get; } = string.IsNullOrWhiteSpace(Message)
        ? throw new ArgumentException("A warning needs a message.", nameof(Message))
        : Message;

    private static bool IsKebabCase(string? code) =>
        !string.IsNullOrEmpty(code)
        && char.IsAsciiLetterLower(code[0])
        && code[^1] != '-'
        && !code.Contains("--", StringComparison.Ordinal)
        && code.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
