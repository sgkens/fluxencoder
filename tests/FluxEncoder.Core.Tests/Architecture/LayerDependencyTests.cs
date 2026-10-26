using System.Reflection;

namespace FluxEncoder.Core.Tests.Architecture;

/// <summary>
/// Guards the layer direction Core ← Infrastructure ← Cli. Core must stay free of
/// process, file-system, and UI dependencies so it can be tested without FFmpeg.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly string[] OuterLayers = ["FluxEncoder.Infrastructure", "flux"];

    [Fact]
    public void Core_DoesNotReferenceOuterLayers()
    {
        var core = Assembly.Load(new AssemblyName("FluxEncoder.Core"));

        var referenced = core.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(referenced, name => OuterLayers.Contains(name, StringComparer.Ordinal));
    }

    [Fact]
    public void Core_ProjectFile_HasNoProjectReferences()
    {
        // Assembly metadata omits unused references, so also check the project file itself.
        var project = FindRepoFile(Path.Combine("src", "FluxEncoder.Core", "FluxEncoder.Core.csproj"));

        Assert.DoesNotContain("<ProjectReference", File.ReadAllText(project), StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above the test output directory.");
    }
}
