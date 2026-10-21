using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Tests.Results;

public sealed class ExitCodeTests
{
    /// <summary>
    /// Pins the public numbering. A failure here means a released contract changed: add a new code instead.
    /// </summary>
    [Theory]
    [InlineData(ExitCode.Success, 0)]
    [InlineData(ExitCode.GeneralFailure, 1)]
    [InlineData(ExitCode.InvalidUsage, 2)]
    [InlineData(ExitCode.InputError, 3)]
    [InlineData(ExitCode.OutputError, 4)]
    [InlineData(ExitCode.DependencyMissing, 5)]
    [InlineData(ExitCode.Unsupported, 6)]
    [InlineData(ExitCode.EncodeFailed, 7)]
    [InlineData(ExitCode.OutputValidationFailed, 8)]
    [InlineData(ExitCode.Timeout, 9)]
    [InlineData(ExitCode.PartialFailure, 10)]
    [InlineData(ExitCode.Cancelled, 130)]
    public void Values_AreStable(ExitCode code, int value)
    {
        Assert.Equal(value, (int)code);
    }

    [Fact]
    public void EveryCode_IsPinned()
    {
        Assert.Equal(12, Enum.GetValues<ExitCode>().Length);
    }

    [Fact]
    public void EveryCategory_MapsToAFailureCode()
    {
        foreach (var category in Enum.GetValues<FailureCategory>())
        {
            var code = category.ToExitCode();

            Assert.NotEqual(ExitCode.Success, code);
            Assert.NotEqual(ExitCode.PartialFailure, code);
        }
    }

    [Theory]
    [InlineData(FailureCategory.OutputExists, ExitCode.OutputError)]
    [InlineData(FailureCategory.OutputIsInput, ExitCode.OutputError)]
    [InlineData(FailureCategory.NetworkError, ExitCode.InputError)]
    [InlineData(FailureCategory.Cancelled, ExitCode.Cancelled)]
    [InlineData(FailureCategory.Internal, ExitCode.GeneralFailure)]
    public void Categories_GroupByUserAction(FailureCategory category, ExitCode expected)
    {
        Assert.Equal(expected, category.ToExitCode());
    }

    [Fact]
    public void OnlyNetworkAndTimeout_AreTransient()
    {
        var transient = Enum.GetValues<FailureCategory>().Where(c => c.IsTransient());

        Assert.Equal([FailureCategory.NetworkError, FailureCategory.Timeout], transient);
    }
}
