using System.Collections.Immutable;

namespace FluxEncoder.Core.Encoding;

/// <summary>
/// One FFmpeg run, as the argument vector passed after the executable name. Each element becomes one
/// <c>ProcessStartInfo.ArgumentList</c> entry, so no quoting or splitting is applied (TSKE004.1).
/// </summary>
public sealed record FFmpegInvocation
{
    public FFmpegInvocation(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var copy = arguments.ToImmutableArray();
        if (copy.IsEmpty)
        {
            throw new ArgumentException("An invocation needs at least one argument.", nameof(arguments));
        }

        if (copy.Any(a => a is null))
        {
            throw new ArgumentException("Arguments cannot be null.", nameof(arguments));
        }

        Arguments = copy;
    }

    public ImmutableArray<string> Arguments { get; }

    /// <summary>
    /// The redacted, shell-quoted command line for display. Secrets are masked; see <see cref="ArgumentRedactor"/>.
    /// </summary>
    public string ToDisplayString(string executable = "ffmpeg") => CommandDisplay.Render(executable, Arguments);

    public override string ToString() => ToDisplayString();

    /// <summary>
    /// Compares argument sequences, not array identity, so plans can be compared in snapshot tests (TSKE005.1).
    /// </summary>
    public bool Equals(FFmpegInvocation? other) =>
        other is not null && Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var argument in Arguments)
        {
            hash.Add(argument, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
