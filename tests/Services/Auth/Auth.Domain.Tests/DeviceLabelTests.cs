using Auth.Domain.RefreshTokens;

namespace Auth.Domain.Tests;

public sealed class DeviceLabelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36", "Chrome · Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0", "Edge · Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/19.1 Safari/605.1.15", "Safari · macOS")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 15.6; rv:145.0) Gecko/20100101 Firefox/145.0", "Firefox · macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 19_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/19.1 Mobile/15E148 Safari/604.1", "Safari · iOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 19_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/142.0.0.0 Mobile/15E148 Safari/604.1", "Chrome · iOS")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 19_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/19.1 Mobile/15E148 Safari/604.1", "Safari · iPadOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 16; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36", "Chrome · Android")]
    [InlineData("Mozilla/5.0 (Linux; Android 16; SM-S931B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/29.0 Chrome/138.0.0.0 Mobile Safari/537.36", "Samsung Internet · Android")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 OPR/125.0.0.0", "Opera · Linux")]
    [InlineData("Mozilla/5.0 (X11; CrOS x86_64 16433.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36", "Chrome · ChromeOS")]
    [InlineData("SomeClient/1.0 (Windows)", "Windows")]
    [InlineData("curl/8.17.0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void UserAgent_NamesTheBrowserAndSystem(string? userAgent, string? label)
        => Assert.Equal(label, DeviceLabels.FromUserAgent(userAgent));

    [Fact]
    public void Label_IsTrimmedAndCut()
    {
        Assert.Null(DeviceLabels.Clean("   "));
        Assert.Equal("Chrome · Windows", DeviceLabels.Clean("  Chrome · Windows "));
        Assert.Equal(DeviceLabels.MaxLength, DeviceLabels.Clean(new string('a', 100))!.Length);
    }

    [Fact]
    public void EveryTokenOfTheFamily_CarriesTheDevice()
    {
        var first = RefreshToken.Issue(Guid.CreateVersion7(), SessionKind.Customer, new byte[RefreshToken.HashLength], Now, "Chrome · Windows");

        var next = first.Rotate(Enumerable.Repeat((byte)1, RefreshToken.HashLength).ToArray(), Now.AddMinutes(15));

        Assert.Equal("Chrome · Windows", first.DeviceLabel);
        Assert.Equal(first.DeviceLabel, next.DeviceLabel);
        Assert.Equal(first.FamilyId, next.FamilyId);
    }
}
