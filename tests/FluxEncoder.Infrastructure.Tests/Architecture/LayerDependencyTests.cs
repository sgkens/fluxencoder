using System.Reflection;

namespace FluxEncoder.Infrastructure.Tests.Architecture;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Infrastructure_DoesNotReferenceCli()
    {
        var infrastructure = Assembly.Load(new AssemblyName("FluxEncoder.Infrastructure"));

        Assert.DoesNotContain(
            infrastructure.GetReferencedAssemblies(),
            a => string.Equals(a.Name, "FluxEncoder.Cli", StringComparison.Ordinal));
    }
}
