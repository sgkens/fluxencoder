using FluxEncoder.Core.Manifests;

namespace FluxEncoder.Core.Tests.Manifests;

public sealed class ManifestVariablesTests
{
    private static string? Lookup(string name) => name switch
    {
        "FLUX_OUT" => "/srv/out",
        "EMPTY" => "",
        "NESTED" => "${FLUX_OUT}",
        _ => null,
    };

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("${FLUX_OUT}/a.mkv", "/srv/out/a.mkv")]
    [InlineData("${MISSING:-out}/a.mkv", "out/a.mkv")]
    [InlineData("${EMPTY:-fallback}", "fallback")]
    [InlineData("${MISSING:-}x", "x")]
    [InlineData("cost $$5 and $5", "cost $5 and $5")]
    [InlineData("trailing $", "trailing $")]
    [InlineData("${NESTED}", "${FLUX_OUT}")]
    public void TryExpand_Substitutes(string text, string expected)
    {
        Assert.True(ManifestVariables.TryExpand(text, Lookup, out var expanded, out var error), error);
        Assert.Equal(expected, expanded);
    }

    [Theory]
    [InlineData("${MISSING}/a.mkv", "Variable 'MISSING' is not set")]
    [InlineData("${EMPTY}", "Variable 'EMPTY' is not set")]
    [InlineData("${FLUX_OUT", "has no closing")]
    [InlineData("${1BAD}", "not a variable reference")]
    [InlineData("${a b}", "not a variable reference")]
    public void TryExpand_RefusesUnresolved(string text, string expected)
    {
        Assert.False(ManifestVariables.TryExpand(text, Lookup, out _, out var error));
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }
}
