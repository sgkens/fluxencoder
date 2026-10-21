using System.Text.Json.Nodes;
using FluxEncoder.Core.Manifests;
using FluxEncoder.Infrastructure.Manifests;

namespace FluxEncoder.Infrastructure.Tests.Manifests;

public sealed class ManifestParserTests
{
    [Fact]
    public void Yaml_KeepsScalarsAsTextWithPositions()
    {
        var (root, error) = ManifestParser.ParseYaml("version: 1\njobs:\n  a:\n    input: no\n    start: 1:30\n    x: ~\n");

        Assert.Null(error);
        var jobs = (ManifestMapping)((ManifestMapping)root!).Entries[1].Value;
        var job = (ManifestMapping)jobs.Entries[0].Value;
        var input = (ManifestScalar)job.Entries[0].Value;
        Assert.Equal(("no", false, new ManifestPosition(4, 12)), (input.Value, input.IsNull, input.Position));
        Assert.Equal("1:30", ((ManifestScalar)job.Entries[1].Value).Value);
        Assert.True(((ManifestScalar)job.Entries[2].Value).IsNull);
    }

    [Fact]
    public void Yaml_QuotedNullIsText()
    {
        var (root, _) = ManifestParser.ParseYaml("a: 'null'\n");

        Assert.False(((ManifestScalar)((ManifestMapping)root!).Entries[0].Value).IsNull);
    }

    [Theory]
    [InlineData("a: &x 1\nb: *x\n", "anchors")]
    [InlineData("a: [&x 1]\n", "anchors")]
    [InlineData("a: !!python/object:os.system x\n", "tags")]
    [InlineData("a: !custom x\n", "tags")]
    [InlineData("a: 1\na: 2\n", "Duplicate key 'a'")]
    [InlineData("a: 1\n---\nb: 2\n", "Only one YAML document")]
    [InlineData("? [a]\n: 1\n", "keys must be plain text")]
    [InlineData("a: [1, 2\n", "Not valid YAML")]
    [InlineData("", "empty")]
    public void Yaml_RefusesUnsafeOrInvalidDocuments(string yaml, string expected)
    {
        var (root, error) = ManifestParser.ParseYaml(yaml);

        Assert.Null(root);
        Assert.Contains(expected, error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Yaml_BillionLaughsIsRefusedBeforeExpansion()
    {
        var yaml = "a: &a [x, x, x, x, x, x, x, x, x]\nb: &b [*a, *a, *a, *a, *a, *a, *a, *a, *a]\nc: [*b, *b, *b, *b, *b, *b, *b, *b, *b]\n";

        var (root, error) = ManifestParser.ParseYaml(yaml);

        Assert.Null(root);
        Assert.Equal(new ManifestPosition(1, 4), error!.Position);
    }

    [Fact]
    public void Yaml_RefusesDeepNesting()
    {
        var yaml = new string('[', 40) + new string(']', 40);

        Assert.Contains("nested more than", ManifestParser.ParseYaml(yaml).Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_AllowsCommentsAndReportsPositions()
    {
        var (root, error) = ManifestParser.ParseJson("{\n  // comment\n  \"version\": 1,\n  \"jobs\": { \"a\": { \"input\": \"é.mkv\", \"x\": null, \"y\": true } },\n}");

        Assert.Null(error);
        var mapping = (ManifestMapping)root!;
        Assert.Equal(new ManifestPosition(3, 3), mapping.Entries[0].Key.Position);
        Assert.Equal("1", ((ManifestScalar)mapping.Entries[0].Value).Value);
        var job = (ManifestMapping)((ManifestMapping)mapping.Entries[1].Value).Entries[0].Value;
        Assert.Equal(new ManifestPosition(4, 38), job.Entries[1].Key.Position);
        Assert.True(((ManifestScalar)job.Entries[1].Value).IsNull);
        Assert.Equal("true", ((ManifestScalar)job.Entries[2].Value).Value);
    }

    [Theory]
    [InlineData("{\"a\": 1, \"a\": 2}", "Duplicate key 'a'")]
    [InlineData("{\"a\": ", "Not valid JSON")]
    [InlineData("{} {}", "Not valid JSON")]
    [InlineData(" \n", "empty")]
    public void Json_RefusesInvalidDocuments(string json, string expected)
    {
        var (root, error) = ManifestParser.ParseJson(json);

        Assert.Null(root);
        Assert.Contains(expected, error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_RefusesDeepNesting()
    {
        var json = new string('[', 40) + new string(']', 40);

        Assert.NotNull(ManifestParser.ParseJson(json).Error);
    }

    [Fact]
    public void YamlWriter_QuotesEveryStringSoHostileValuesRoundTrip()
    {
        string[] hostile = ["no", "1:30", "*alias", "&anchor x", "!!python/object x", "key: value", "- item", "# comment",
            "line\nbreak", "esc\u001b[31m", "null", "", " padded ", "{a: 1}", "'quoted'", "\"dq\"", "\u2028sep"];
        var tags = new JsonObject();
        foreach (var (value, i) in hostile.Select((v, i) => (v, i)))
        {
            tags[$"k{i}"] = value;
        }

        tags["no"] = "key that YAML 1.1 reads as false";
        tags["weird key: x"] = 1;
        var document = new JsonObject { ["title"] = "x", ["count"] = 3, ["ok"] = true, ["none"] = null, ["tags"] = tags, ["empty"] = new JsonArray() };

        var yaml = YamlWriter.Write(document);
        var (root, error) = ManifestParser.ParseYaml(yaml);

        Assert.Null(error);
        var parsedTags = (ManifestMapping)((ManifestMapping)root!).Entries.Single(e => e.Key.Value == "tags").Value;
        foreach (var (value, i) in hostile.Select((v, i) => (v, i)))
        {
            var scalar = (ManifestScalar)parsedTags.Entries.Single(e => e.Key.Value == $"k{i}").Value;
            Assert.Equal(value, scalar.Value);
            Assert.False(scalar.IsNull);
        }

        Assert.Contains("\"no\": ", yaml, StringComparison.Ordinal);
        Assert.Contains("\"weird key: x\": 1", yaml, StringComparison.Ordinal);
        Assert.Contains("count: 3", yaml, StringComparison.Ordinal);
        Assert.Contains("ok: true", yaml, StringComparison.Ordinal);
        Assert.Contains("empty: []", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", yaml, StringComparison.Ordinal);
    }
}
