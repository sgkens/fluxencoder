using System.Text;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// Renders a redacted command line for <c>--dry-run</c>, logs, and error reports. The result is for people to
/// read and copy into a POSIX shell; it is never parsed back or executed by FluxEncoder.
/// </summary>
public static class CommandDisplay
{
    public static string Render(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        var builder = new StringBuilder();
        builder.Append(Quote(executable));

        foreach (var argument in ArgumentRedactor.Redact(arguments))
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Single-quotes an argument when it contains anything a POSIX shell would interpret.
    /// </summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        if (argument.Length > 0 && argument.All(IsSafe))
        {
            return argument;
        }

        return "'" + argument.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
    }

    private static bool IsSafe(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or ':' or '=' or ',' or '+' or '@' or '%';
}
