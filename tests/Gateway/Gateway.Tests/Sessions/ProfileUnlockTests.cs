using System.Net;

namespace Gateway.Tests.Sessions;

/// <summary>Opening a locked profile through the gateway renews the session at once.</summary>
[Collection(nameof(ValkeyCollection))]
public sealed class ProfileUnlockTests(ValkeyFixture valkey)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARightPin_RenewsTheSession_SoTheNextRequestCarriesTheProfile()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();
        var profile = Guid.CreateVersion7();
        var refreshesBefore = harness.Auth.RefreshCalls;

        var unlocked = await browser.SendAsync(HttpMethod.Post, $"/session/profiles/{profile}/unlock", new { pin = FakeAuthService.Pin });

        Assert.Equal(HttpStatusCode.NoContent, unlocked.StatusCode);
        Assert.True(unlocked.Headers.CacheControl?.NoStore);
        Assert.Equal(profile, Assert.Single(harness.Auth.ProfileUnlocks).ProfileId);
        Assert.Equal(refreshesBefore + 1, harness.Auth.RefreshCalls);
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(HttpMethod.Get, "/session")).StatusCode);
    }

    [Fact]
    public async Task AWrongPin_IsPassedOn_AndTheSessionStaysAsItWas()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();
        var refreshesBefore = harness.Auth.RefreshCalls;

        var refused = await browser.SendAsync(HttpMethod.Post, $"/session/profiles/{Guid.CreateVersion7()}/unlock", new { pin = "0000" });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Profile.PinIncorrect", await refused.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(refreshesBefore, harness.Auth.RefreshCalls);
    }

    [Fact]
    public async Task Unlocking_NeedsTheSession_AndTheCsrfToken()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(HttpMethod.Post, $"/session/profiles/{Guid.CreateVersion7()}/unlock", new { pin = FakeAuthService.Pin }, withCsrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await harness.NewBrowser().SendAsync(HttpMethod.Post, $"/session/profiles/{Guid.CreateVersion7()}/unlock", new { pin = FakeAuthService.Pin })).StatusCode);
        Assert.Empty(harness.Auth.ProfileUnlocks);
    }
}
