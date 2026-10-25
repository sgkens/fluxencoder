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
    public void Version_ComesFromAssemblyMetadata()
    {
        var cli = typeof(CliApplication).Assembly;
        var informational = cli.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CliApplication.BuildRootCommand()
            .Parse(["--version"])
            .Invoke(new InvocationConfiguration { Output = output, Error = error });

        Assert.Equal(0, exitCode);
        Assert.NotNull(informational);
        Assert.StartsWith("0.1.0", informational, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeAOT", informational, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("0.1.0", output.ToString().Trim(), StringComparison.Ordinal);
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

    [Fact]
    public void RootCommand_RegistersOnlyImplementedCommandsAndNoneHidden()
    {
        var root = CliApplication.BuildRootCommand();

        Assert.Equal(["encode", "merge", "config", "history", "remote", "apply", "info"], root.Subcommands.Select(c => c.Name));
        Assert.All(root.Subcommands, c => Assert.False(c.Hidden));
    }

    [Fact]
    public void BuildingTwice_DoesNotShareOptionState()
    {
        var first = CliApplication.BuildRootCommand().Subcommands.Single(c => c.Name == "encode");
        var second = CliApplication.BuildRootCommand().Subcommands.Single(c => c.Name == "encode");

        Assert.NotSame(first.Options[0], second.Options[0]);
    }
}
