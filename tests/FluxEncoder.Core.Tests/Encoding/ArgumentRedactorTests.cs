using FluxEncoder.Core.Encoding;

namespace FluxEncoder.Core.Tests.Encoding;

public sealed class ArgumentRedactorTests
{
    [Theory]
    [InlineData("rtmp://a.rtmp.youtube.com/live2/abcd-efgh-ijkl", "rtmp://a.rtmp.youtube.com/live2/***")]
    [InlineData("rtmps://live-api.facebook.com:443/rtmp/FB-123?s_bl=1&s_sc=2", "rtmps://live-api.facebook.com:443/rtmp/***?s_bl=1&s_sc=2")]
    [InlineData("rtmp://host/app", "rtmp://host/app")]
    [InlineData("rtmp://host/app/", "rtmp://host/app/")]
    [InlineData("srt://host:9000?passphrase=hunter22&latency=200", "srt://host:9000?passphrase=***&latency=200")]
    [InlineData("https://user:pa55@cdn.example/v.m3u8", "https://user:***@cdn.example/v.m3u8")]
    [InlineData("https://token@cdn.example/v.m3u8", "https://***@cdn.example/v.m3u8")]
    [InlineData("https://cdn.example/v.m3u8?X-Amz-Signature=abc&Expires=1", "https://cdn.example/v.m3u8?X-Amz-Signature=***&Expires=1")]
    [InlineData("https://cdn.example/v.m3u8?api_key=1#frag", "https://cdn.example/v.m3u8?api_key=***#frag")]
    [InlineData("https://cdn.example/a/b/video.mp4", "https://cdn.example/a/b/video.mp4")]
    [InlineData("/home/user/My Movie.mkv", "/home/user/My Movie.mkv")]
    [InlineData("libx264", "libx264")]
    public void RedactUrl(string input, string expected)
    {
        Assert.Equal(expected, ArgumentRedactor.RedactUrl(input));
    }

    [Fact]
    public void Redact_MasksSecretOptionValuesIncludingStreamSpecifiers()
    {
        string[] arguments =
        [
            "-headers", "Authorization: Bearer abc", "-i", "https://u:p@h/x",
            "-encryption_key:v", "0011", "-c:v", "libx264", "-passphrase", "secret", "out.ts",
        ];

        Assert.Equal(
            ["-headers", "***", "-i", "https://u:***@h/x", "-encryption_key:v", "***", "-c:v", "libx264", "-passphrase", "***", "out.ts"],
            ArgumentRedactor.Redact(arguments));
    }

    [Fact]
    public void Redact_DoesNotChangeTheInvocation()
    {
        var invocation = new FFmpegInvocation(["-i", "in.mkv", "rtmp://host/app/key"]);

        _ = invocation.ToDisplayString();

        Assert.Equal("rtmp://host/app/key", invocation.Arguments[2]);
    }

    [Fact]
    public void Display_QuotesAndRedacts()
    {
        var invocation = new FFmpegInvocation(["-i", "My Movie's Cut.mkv", "-vf", "scale=1280:-2", "rtmp://host/live/key123"]);

        Assert.Equal(
            "ffmpeg -i 'My Movie'\\''s Cut.mkv' -vf scale=1280:-2 'rtmp://host/live/***'",
            invocation.ToDisplayString());
        Assert.Equal(invocation.ToDisplayString(), invocation.ToString());
    }

    [Theory]
    [InlineData("", "''")]
    [InlineData("plain", "plain")]
    [InlineData("a b", "'a b'")]
    [InlineData("$HOME", "'$HOME'")]
    [InlineData("a;rm -rf", "'a;rm -rf'")]
    [InlineData("it's", "'it'\\''s'")]
    public void Quote(string argument, string expected)
    {
        Assert.Equal(expected, CommandDisplay.Quote(argument));
    }
}
