using System.Reflection;

namespace FluxEncoder.Cli.Tests.Architecture;

/// <summary>
/// Guards against the concept-era pattern of redefining a type in a later revision
/// (for example two <c>EncodeCommand</c> or <c>HardwareVendor</c> definitions in different namespaces).
/// </summary>
public sealed class SingleDefinitionTests
{
    private static readonly string[] ProductionAssemblies =
        ["FluxEncoder.Core", "FluxEncoder.Infrastructure", "FluxEncoder.Cli"];

    [Fact]
    public void PublicTypeNames_AreUniqueAcrossProductionAssemblies()
    {
        var duplicates = ProductionAssemblies
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.IsNested)
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(t => t.FullName))}")
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Solution_HasOneEntryPoint()
    {
        var entryPoints = ProductionAssemblies
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .Where(assembly => assembly.EntryPoint is not null)
            .Select(assembly => assembly.GetName().Name)
            .ToList();

        Assert.Equal(["FluxEncoder.Cli"], entryPoints);
    }
}
