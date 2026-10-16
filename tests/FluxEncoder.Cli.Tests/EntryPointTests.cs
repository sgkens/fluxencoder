using System.CommandLine;
using System.Reflection;

namespace FluxEncoder.Cli.Tests;

public sealed class EntryPointTests
{
    [Fact]
    public void CliAssembly_HasEntryPoint()
    {
        var cli = Assembly.Load(new AssemblyName("FluxEncoder.Cli"));

        Assert.NotNull(cli.EntryPoint);
    }

    [Fact]
    public void Help_WritesToOutput_AndSucceeds()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApplication.BuildRootCommand()
            .Parse(["--help"])
            .Invoke(new InvocationConfiguration { Output = output, Error = error });

        Assert.Equal(0, exitCode);
        Assert.Contains("FluxEncoder", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void UnknownCommand_FailsWithNonZeroExitCode()
    {
        var exitCode = CliApplication.BuildRootCommand()
            .Parse(["no-such-command"])
            .Invoke(new InvocationConfiguration { Output = TextWriter.Null, Error = TextWriter.Null });

        Assert.NotEqual(0, exitCode);
    }
}
