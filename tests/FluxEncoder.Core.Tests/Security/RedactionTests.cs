using FluxEncoder.Core.Security;

namespace FluxEncoder.Core.Tests.Security;

[Collection(nameof(RedactionTests))]
public sealed class RedactionTests : IDisposable
{
    public RedactionTests() => Redaction.Reset();

    public void Dispose() => Redaction.Reset();

    [Theory]
    [InlineData("Could not read 'https://alice:hunter22@cdn.example/v.m3u8'.", "Could not read 'https://alice:***@cdn.example/v.m3u8'.")]
    [InlineData("[tcp] Connection to tcp://bob:pw@10.0.0.1:1935 failed", "[tcp] Connection to tcp://bob:***@10.0.0.1:1935 failed")]
    [InlineData("pushing to rtmp://live.example/app/streamkey123 now", "pushing to rtmp://live.example/app/*** now")]
    [InlineData("GET https://x/a?token=abc&b=1 404", "GET https://x/a?token=***&b=1 404")]
    [InlineData("Authorization: Bearer eyJhbGciOi", "Authorization: ***")]
    [InlineData("proxy-authorization: Basic Zm9vOmJhcg==", "proxy-authorization: ***")]
    [InlineData("/home/user/My Movie.mkv", "/home/user/My Movie.mkv")]
    [InlineData("", "")]
    public void Text_MasksRecognisableSecrets(string input, string expected)
    {
        Assert.Equal(expected, Redaction.Text(input));
    }

    [Fact]
    public void RegisteredSecrets_AreMaskedRawAndPercentEncoded()
    {
        Redaction.Register("p@ss word/1");

        Assert.Equal("failed with *** and ***", Redaction.Text("failed with p@ss word/1 and p%40ss%20word%2F1"));
    }

    [Fact]
    public void ShortSecrets_AreNotRegistered()
    {
        Redaction.Register("abc");

        Assert.Equal("abc abcdef", Redaction.Text("abc abcdef"));
    }

    [Fact]
    public void Text_IsIdempotent()
    {
        var once = Redaction.Text("https://u:p@h/x?key=1");

        Assert.Equal(once, Redaction.Text(once));
    }
}
