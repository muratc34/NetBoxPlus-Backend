using System.Net;
using System.Text.Json;

namespace Gateway.Tests.Sessions;

/// <summary>The account's list of devices through the gateway, and signing a device out at once.</summary>
[Collection(nameof(ValkeyCollection))]
public sealed class DeviceTests(ValkeyFixture valkey)
{
    private const string CustomerCookie = "__Host-nbp-session";
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignIn_PassesTheBrowsersUserAgentOn_AndTheListComesBackAsItIs()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        browser.Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Chrome);
        await browser.SignInAsync();

        var response = await browser.SendAsync(HttpMethod.Get, "/session/devices");

        Assert.Equal(Chrome, Assert.Single(harness.Auth.UserAgents));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        var only = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(Assert.Single(harness.Auth.Sessions), only.GetProperty("id").GetGuid());
        Assert.True(only.GetProperty("current").GetBoolean());
    }

    [Fact]
    public async Task EndingAnotherDevice_SignsItOutAtOnce_NotWhenItsTokenRunsOut()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var laptop = harness.NewBrowser();
        var phone = harness.NewBrowser();
        await laptop.SignInAsync();
        await phone.SignInAsync();
        var phoneSession = harness.Auth.Sessions.Last();

        var ended = await laptop.SendAsync(HttpMethod.Delete, $"/session/devices/{phoneSession}");

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal([phoneSession], harness.Auth.EndedSessions);
        // The phone's access token has most of its 15 minutes left, yet its session is gone.
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.SendAsync(HttpMethod.Get, "/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.SendAsync(HttpMethod.Get, "/session")).StatusCode);
        Assert.False(ended.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task EndingThisDevice_WorksLikeSigningOut()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();
        var keysBefore = (await harness.ValkeyKeysAsync("gateway:session:*")).Count;

        var ended = await browser.SendAsync(HttpMethod.Delete, $"/session/devices/{harness.Auth.Sessions.Single()}");

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Contains(ended.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(CustomerCookie + "=;", StringComparison.Ordinal));
        Assert.Equal(keysBefore - 1, (await harness.ValkeyKeysAsync("gateway:session:*")).Count);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.SendAsync(HttpMethod.Get, "/session")).StatusCode);
    }

    [Fact]
    public async Task EndingTheOthers_SignsThemOut_AndKeepsThisOne()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var (laptop, phone, tablet) = (harness.NewBrowser(), harness.NewBrowser(), harness.NewBrowser());
        await laptop.SignInAsync();
        await phone.SignInAsync();
        await tablet.SignInAsync();

        var ended = await laptop.SendAsync(HttpMethod.Delete, "/session/devices");

        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        using var json = JsonDocument.Parse(await ended.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(2, json.RootElement.GetProperty("ids").GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.SendAsync(HttpMethod.Get, "/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tablet.SendAsync(HttpMethod.Get, "/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.SendAsync(HttpMethod.Get, "/session")).StatusCode);
    }

    [Fact]
    public async Task EndingADevice_NeedsTheSessionsCsrfToken_AndAnUnknownOneIsPassedOnAsNotFound()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();

        var forged = await browser.SendAsync(HttpMethod.Delete, "/session/devices", withCsrf: false);
        var unknown = await browser.SendAsync(HttpMethod.Delete, $"/session/devices/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Empty(harness.Auth.EndedSessions);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("Session.NotFound", await unknown.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutASession_TheDevicesAreNotShown()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await harness.NewBrowser().SendAsync(HttpMethod.Get, "/session/devices")).StatusCode);
    }
}
