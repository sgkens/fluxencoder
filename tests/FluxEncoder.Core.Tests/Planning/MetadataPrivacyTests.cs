using FluxEncoder.Core.Encoding;
using FluxEncoder.Core.Media;
using FluxEncoder.Core.Planning;
using FluxEncoder.Core.Results;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Planning;

public sealed class MetadataPrivacyTests
{
    [Theory]
    [InlineData("location", true)]
    [InlineData("LOCATION-eng", true)]
    [InlineData("com.apple.quicktime.location.ISO6709", true)]
    [InlineData("com.apple.quicktime.make", true)]
    [InlineData("com.apple.quicktime.model", true)]
    [InlineData("com.apple.quicktime.software", true)]
    [InlineData("com.android.manufacturer", true)]
    [InlineData("com.android.version", true)]
    [InlineData("camera_serial_number", true)]
    [InlineData("GPSCoordinates", true)]
    [InlineData("modelname", false)]
    [InlineData("gps_coordinates", true)]
    [InlineData("owner", true)]
    [InlineData("title", false)]
    [InlineData("artist", false)]
    [InlineData("comment", false)]
    [InlineData("description", false)]
    [InlineData("encoder", false)]
    [InlineData("software", false)]
    [InlineData("creation_time", false)]
    [InlineData("com.apple.quicktime.creationdate", false)]
    public void IsPrivate_CoversLocationAndDeviceTagsOnly(string key, bool expected)
    {
        Assert.Equal(expected, MetadataPrivacy.IsPrivate(key));
    }

    private static MediaInfo Phone() => Movie() with
    {
        Tags = MediaTags.Create([new("title", "Trip"), new("location", "+48.8577+002.2950/"), new("com.apple.quicktime.make", "Apple"), new("comment", "mine")]),
    };

    private static string Arguments(EncodingJob job) =>
        string.Join(' ', EncodePlanner.Plan(job, Input(Phone()), Context()).Value!.Plan!.Invocations[^1].Arguments);

    [Fact]
    public void Planner_RemovesPrivateTagsByDefaultAndSaysSo()
    {
        var planned = EncodePlanner.Plan(Job("/media/out/a.mkv"), Input(Phone()), Context()).Value!;
        var arguments = string.Join(' ', planned.Plan!.Invocations[^1].Arguments);

        Assert.Contains("-metadata:g com.apple.quicktime.make= -metadata:g location=", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("title=", arguments, StringComparison.Ordinal);
        var warning = Assert.Single(planned.Warnings, w => w.Code == WarningCodes.PrivateMetadataRemoved);
        Assert.Contains("(com.apple.quicktime.make, location)", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_KeepsPrivateTagsWhenAsked()
    {
        var job = Job("/media/out/a.mkv") with { Streams = StreamSelection.Default with { PrivateMetadata = true } };

        Assert.DoesNotContain("-metadata:g", Arguments(job), StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_DropsEverythingWithNoMetadata()
    {
        var job = Job("/media/out/a.mkv") with { Streams = StreamSelection.Default with { Metadata = false } };
        var arguments = Arguments(job);

        Assert.Contains("-map_metadata:g -1", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("-metadata:g", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_AddsNothingWithoutPrivateTags()
    {
        var planned = EncodePlanner.Plan(Job("/media/out/a.mkv"), Input(), Context()).Value!;

        Assert.DoesNotContain(planned.Warnings, w => w.Code == WarningCodes.PrivateMetadataRemoved);
    }
}
