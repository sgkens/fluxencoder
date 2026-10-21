using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Results;

namespace FluxEncoder.Core.Tests.Encoding;

public sealed class PolicyTests
{
    private static readonly JobFailure Network = new(FailureCategory.NetworkError, "Connection reset.");
    private static readonly JobFailure Encode = new(FailureCategory.EncodeFailed, "FFmpeg exited with code 1.");

    [Fact]
    public void Defaults_AreSafeWithoutFlags()
    {
        var job = new EncodingJob
        {
            InputPath = "in.mkv",
            OutputPath = "out.mkv",
            Video = new VideoTarget.StreamCopy(),
            Audio = new AudioTarget.StreamCopy(),
        };

        Assert.Same(JobPolicy.Default, job.Policy);
        Assert.Equal(OverwritePolicy.Never, job.Policy.Overwrite);
        Assert.Equal(PartialOutputPolicy.Delete, job.Policy.PartialOutput);
        Assert.Equal(1, job.Policy.Retry.MaxAttempts);
        Assert.Equal(BatchFailurePolicy.Continue, default(BatchFailurePolicy));
    }

    [Fact]
    public void Policy_RejectsUndefinedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobPolicy.Default with { Overwrite = (OverwritePolicy)9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => JobPolicy.Default with { PartialOutput = (PartialOutputPolicy)9 });
        Assert.Throws<ArgumentNullException>(() => JobPolicy.Default with { Retry = null! });
    }

    [Fact]
    public void Retry_None_NeverRetries()
    {
        Assert.False(RetryPolicy.None.ShouldRetry(Network, 1));
    }

    [Fact]
    public void Retry_OnlyTransientFailuresWithinLimit()
    {
        var policy = new RetryPolicy(3, TimeSpan.FromSeconds(2));

        Assert.True(policy.ShouldRetry(Network, 1));
        Assert.True(policy.ShouldRetry(Network, 2));
        Assert.False(policy.ShouldRetry(Network, 3));
        Assert.False(policy.ShouldRetry(Encode, 1));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(6, 60)]
    [InlineData(100, 60)]
    public void Retry_DelayDoublesAndCaps(int attempt, int seconds)
    {
        var policy = new RetryPolicy(200, TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(seconds), policy.GetDelay(attempt));
    }

    [Fact]
    public void Retry_RejectsInvalidSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(0, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(2, TimeSpan.FromSeconds(-1)));
    }
}
