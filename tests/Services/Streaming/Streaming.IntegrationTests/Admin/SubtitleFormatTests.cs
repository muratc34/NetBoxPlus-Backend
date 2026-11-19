using Streaming.Application.Videos.Subtitles;

namespace Streaming.IntegrationTests.Admin;

/// <summary>Reading uploaded subtitles; no container needed.</summary>
public sealed class SubtitleFormatTests
{
    [Fact]
    public void Srt_BecomesWebVtt()
    {
        var srt = (char)0xFEFF + "1\r\n00:00:01,000 --> 00:00:02,500\r\nHello.\r\n\r\n2\r\n0:01:02,040 --> 0:01:03,000\r\nA café line\r\nand more\r\n";

        Assert.Equal(
            "WEBVTT\n\n00:00:01.000 --> 00:00:02.500\nHello.\n\n00:01:02.040 --> 00:01:03.000\nA café line\nand more\n",
            SubtitleFormat.ToWebVtt(srt));
    }

    [Fact]
    public void WebVtt_IsKeptAsItIs()
    {
        var vtt = "WEBVTT\r\n\r\n00:01.000 --> 00:02.000 align:start\r\n<i>Hello</i>\r\n";

        Assert.Equal("WEBVTT\n\n00:01.000 --> 00:02.000 align:start\n<i>Hello</i>\n", SubtitleFormat.ToWebVtt(vtt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not subtitles at all")]
    [InlineData("WEBVTT\n\nno cue here\n")]
    [InlineData("1\n00:00:01,000 --> 00:00:02,000\n")]
    [InlineData("1\n00:00:01.000 --> 00:00:02.000\nDots are not SRT.\n")]
    [InlineData("1\n00:00:01,000 --> 00:00:02,000\nFine.\n\n2\nbroken timing\nText.\n")]
    [InlineData("1\n00:61:01,000 --> 00:00:02,000\nNo such minute.\n")]
    public void AnythingElse_IsRefused(string text) => Assert.Null(SubtitleFormat.ToWebVtt(text));
}
