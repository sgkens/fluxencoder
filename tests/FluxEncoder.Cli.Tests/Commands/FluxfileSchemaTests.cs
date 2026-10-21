using System.CommandLine;
using System.CommandLine.Completions;
using System.Text.Json;
using FluxEncoder.Cli.Commands;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Core.Manifests;

namespace FluxEncoder.Cli.Tests.Commands;

/// <summary>
/// Keeps <c>docs/schema/fluxfile-v1.schema.json</c> in step with the options <c>apply</c> accepts, so editor
/// validation never disagrees with FluxEncoder.
/// </summary>
public sealed class FluxfileSchemaTests
{
    private static readonly Command Encode = EncodeCommand.Define(CliHost.Console, new CliSession()).Command;

    private static JsonElement SchemaOptions()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "fluxencoder.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "docs", "schema", "fluxfile-v1.schema.json")));
        return schema.RootElement.GetProperty("$defs").GetProperty("optionProperties").GetProperty("properties").Clone();
    }

    [Fact]
    public void Schema_ListsExactlyTheOptionsApplyAccepts()
    {
        var accepted = ApplyCommand.ManifestOptions(Encode);
        var documented = SchemaOptions().EnumerateObject().Select(p => p.Name);

        Assert.Equal(accepted.Keys.Order(StringComparer.Ordinal), documented.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Schema_FlagsAreBooleans()
    {
        var options = SchemaOptions();

        foreach (var (name, kind) in ApplyCommand.ManifestOptions(Encode))
        {
            var isFlag = options.GetProperty(name).TryGetProperty("$ref", out var reference) && reference.GetString() == "#/$defs/flag";
            Assert.True(isFlag == (kind == FluxOptionKind.Flag), $"'{name}' is a {kind} option but the schema says otherwise.");
        }
    }

    [Fact]
    public void Schema_EnumsMatchTheAcceptedValues()
    {
        var options = SchemaOptions();

        foreach (var option in Encode.Options.Where(o => o.Name.StartsWith("--", StringComparison.Ordinal)))
        {
            var name = option.Name[2..];
            if (!options.TryGetProperty(name, out var property) || !property.TryGetProperty("enum", out var values))
            {
                continue;
            }

            var accepted = option.GetCompletions(CompletionContext.Empty).Select(c => c.Label).ToList();
            Assert.Equal(accepted.Order(StringComparer.Ordinal), values.EnumerateArray().Select(v => v.GetString()!).Order(StringComparer.Ordinal));
        }
    }
}
