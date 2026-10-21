using System.Xml.Linq;
using FluxEncoder.Core.Formats;
using FluxEncoder.Core.Media;
using static FluxEncoder.Core.Tests.Planning.PlannerFixture;

namespace FluxEncoder.Core.Tests.Formats;

public sealed class MediaReportTests
{
    private const string Hostile = "<script>alert(1)</script> | **x** [a](http://e) \u001b[31m";

    private static MediaInfo Media() => Movie() with
    {
        Source = "/media/<b onclick=x>clip.mkv",
        Tags = MediaTags.Create([new("title", Hostile), new("location", "+48.8577+002.2950/"), new("comment", "kept")]),
    };

    [Fact]
    public void Create_HidesPrivateTagsUnlessAsked()
    {
        var report = MediaReport.Create(Media());

        Assert.Equal(["comment", "title"], report.Tags.Select(t => t.Key));
        Assert.Equal(1, report.HiddenTags);
        Assert.Equal(3, MediaReport.Create(Media(), showPrivate: true).Tags.Count);
    }

    [Fact]
    public void Json_CarriesStructuredValues()
    {
        var json = MediaReport.Create(Media()).ToJson();

        Assert.Equal(6000.0, json["duration"]!.GetValue<double>());
        Assert.Equal(Hostile, json["tags"]!["title"]!.GetValue<string>());
        Assert.Equal(1, json["hiddenTags"]!.GetValue<int>());
        Assert.Equal("video", json["streams"]![0]!["kind"]!.GetValue<string>());
        Assert.Null(json["tags"]!["location"]);
    }

    [Fact]
    public void Markdown_EscapesEveryCell()
    {
        var markdown = MediaReport.Create(Media()).ToMarkdown();

        Assert.Contains("| title | \\<script\\>alert\\(1\\)\\</script\\> \\| \\*\\*x\\*\\* \\[a\\]\\(http://e\\) \\[31m |", markdown, StringComparison.Ordinal);
        Assert.StartsWith("## \\<b onclick=x\\>clip.mkv", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", markdown, StringComparison.Ordinal);
        Assert.Contains("1 location or device tag is hidden", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_HasNoActiveContentAndForbidsAllResources()
    {
        var html = MediaReport.Create(Media()).ToHtml();

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b ", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("<title>&lt;b onclick=x&gt;clip.mkv</title>", html, StringComparison.Ordinal);
        Assert.Contains("content=\"default-src 'none'; style-src 'unsafe-inline'\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Badge_SummarizesTheFile()
    {
        var report = MediaReport.Create(Media());

        Assert.Equal("3840\u00D72160 HEVC \u00B7 TRUEHD 8 ch \u00B7 1:40:00", report.Headline());
        Assert.Equal("svg", XDocument.Parse(report.ToBadge()).Root!.Name.LocalName);
    }
}
