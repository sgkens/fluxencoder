using System.Xml.Linq;
using FluxEncoder.Core.Formats;

namespace FluxEncoder.Core.Tests.Formats;

public sealed class FormatEncodingTests
{
    [Theory]
    [InlineData("Movie (2024).mkv", "Movie (2024).mkv")]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("\" onload=\"x", "&quot; onload=&quot;x")]
    [InlineData("it's & more", "it&#39;s &amp; more")]
    [InlineData("&amp;", "&amp;amp;")]
    [InlineData("bell\u0007nul\u0000", "bell\uFFFDnul\uFFFD")]
    [InlineData("emoji \U0001F3AC ok", "emoji \U0001F3AC ok")]
    [InlineData("tab\tline\n", "tab\tline\n")]
    [InlineData(null, "")]
    public void Markup_EncodesHostileText(string? input, string expected)
    {
        Assert.Equal(expected, MarkupText.Encode(input));
    }

    [Fact]
    public void Markup_ReplacesUnpairedSurrogates()
    {
        // Built in code: theory data cannot carry an unpaired surrogate through serialization.
        Assert.Equal("lone\uFFFDsurrogate\uFFFD", MarkupText.Encode("lone" + '\uD800' + "surrogate" + '\uDC00'));
    }

    [Theory]
    [InlineData("plain title", "plain title")]
    [InlineData("a | b", "a \\| b")]
    [InlineData("**bold** _it_ `code`", "\\*\\*bold\\*\\* \\_it\\_ \\`code\\`")]
    [InlineData("[click](https://evil.example)", "\\[click\\]\\(https://evil.example\\)")]
    [InlineData("<img src=x onerror=alert(1)>", "\\<img src=x onerror=alert\\(1\\)\\>")]
    [InlineData("&lt;b&gt;", "\\&lt;b\\&gt;")]
    [InlineData("line\nbreak\r\nend", "line break  end")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("$x$ ~~gone~~ #1 !", "\\$x\\$ \\~\\~gone\\~\\~ \\#1 \\!")]
    [InlineData("esc\u001b[31m", "esc\\[31m")]
    [InlineData(null, "")]
    public void Markdown_EscapesEveryInlineConstruct(string? input, string expected)
    {
        Assert.Equal(expected, MarkdownText.Cell(input));
    }

    [Fact]
    public void Badge_IsWellFormedSvgWithoutActiveContent()
    {
        var svg = SvgBadge.Render("codec", "<script>alert(1)</script>\" onload=\"x\u0000");

        var document = XDocument.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(ns + "svg", document.Root!.Name);
        Assert.DoesNotContain(document.Descendants(), e => e.Name.LocalName == "script");
        Assert.DoesNotContain(document.Descendants().Attributes(), a => a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(document.Descendants().Attributes(), a => a.Name.LocalName == "href");
        Assert.Contains(document.Descendants(ns + "text"), t => t.Value.StartsWith("<script>", StringComparison.Ordinal));
    }

    [Fact]
    public void Badge_ShortensLongValues()
    {
        var svg = SvgBadge.Render("title", new string('x', 500));

        Assert.Contains(new string('x', 63) + "\u2026", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 64), svg, StringComparison.Ordinal);
    }
}
