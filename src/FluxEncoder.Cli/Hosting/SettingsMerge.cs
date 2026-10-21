using System.CommandLine;
using FluxEncoder.Infrastructure.Configuration;

namespace FluxEncoder.Cli.Hosting;

/// <summary>
/// Fills options the user did not type from the selected preset and then from <c>settings.json</c>.
/// </summary>
/// <remarks>
/// Precedence: typed flag, then <c>--preset</c>, then <c>settings.json</c> <c>encode</c> defaults, then the built-in
/// default. Values are appended as ordinary tokens and the command line is parsed again, so settings go through
/// exactly the same parsing and validation as typed flags and can never express something a flag cannot.
/// </remarks>
internal static class SettingsMerge
{
    /// <summary>
    /// Options that only make sense per invocation, so they cannot come from settings or presets.
    /// </summary>
    private static readonly HashSet<string> PerInvocation = new(StringComparer.Ordinal)
    {
        "--output", "--dry-run", "--json", "--preset", "--no-config",
    };

    public sealed record Outcome(IReadOnlyList<string> Arguments, IReadOnlyList<string> Notices, IReadOnlyList<string> Provenance, string? Error);

    public static Outcome Apply(ParseResult first, Command command, IReadOnlyList<string> arguments, FluxSettings settings, string? presetName, bool useSettings)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(settings);

        var values = new Dictionary<string, (string Value, string Source)>(StringComparer.Ordinal);

        if (useSettings)
        {
            foreach (var (key, value) in settings.Encode)
            {
                values[key] = (value, "settings.json");
            }
        }

        if (presetName is not null)
        {
            if (!settings.Presets.TryGetValue(presetName, out var preset))
            {
                var available = settings.Presets.Count == 0
                    ? "none are defined; create some with 'config init'"
                    : "available: " + string.Join(", ", settings.Presets.Keys.Order(StringComparer.OrdinalIgnoreCase));
                return new Outcome(arguments, [], [], $"Unknown preset '{presetName}' ({available}).");
            }

            foreach (var (key, value) in preset.Options)
            {
                values[key] = (value, $"preset '{presetName}'");
            }
        }

        var extras = new List<string>();
        var notices = new List<string>();
        var provenance = new List<string>();

        foreach (var (key, (value, source)) in values.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            var name = "--" + key;
            var option = command.Options.FirstOrDefault(o => o.Name == name || o.Aliases.Contains(name));
            if (option is null)
            {
                notices.Add($"{source}: '{key}' is not an encode option; ignored.");
                continue;
            }

            if (PerInvocation.Contains(option.Name))
            {
                notices.Add($"{source}: '{key}' cannot be preset; give it on the command line. Ignored.");
                continue;
            }

            if (first.GetResult(option) is { Implicit: false })
            {
                continue;
            }

            if (option.ValueType == typeof(bool))
            {
                if (value == "true")
                {
                    extras.Add(option.Name);
                    provenance.Add($"{option.Name} ({source})");
                }
                else if (value != "false")
                {
                    notices.Add($"{source}: '{key}' takes true or false; ignored.");
                }

                continue;
            }

            extras.AddRange([option.Name, value]);
            provenance.Add($"{option.Name} {value} ({source})");
        }

        return new Outcome([.. arguments, .. extras], notices, provenance, null);
    }
}
