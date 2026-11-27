using System.Net;
using System.Text.Json;

namespace Gateway.Tests.Sessions;

/// <summary>Signing in with a passkey through the gateway, and the account's passkeys.</summary>
[Collection(nameof(ValkeyCollection))]
public sealed class PasskeyTests(ValkeyFixture valkey)
{
    private const string CustomerCookie = "__Host-nbp-session";
    private const string AdminCookie = "__Host-nbp-admin";

    private static readonly object Answer = new { challengeId = FakeAuthService.PasskeyChallenge, credential = new { id = "AAAA", type = "public-key" } };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APasskeySignIn_OpensASession_LikeAPasswordOne()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        browser.Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (iPhone) Safari/604.1");

        var options = await browser.SendAsync(HttpMethod.Post, "/session/passkey/options");
        var signedIn = await browser.SendAsync(HttpMethod.Post, "/session/passkey", Answer);
        var body = await signedIn.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.True(options.Headers.CacheControl?.NoStore);
        Assert.Contains("challengeId", await options.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.StartsWith(CustomerCookie + "=", Assert.Single(signedIn.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", body, StringComparison.Ordinal);
        Assert.Equal("passkey@example.com", JsonDocument.Parse(body).RootElement.GetProperty("email").GetString());
        Assert.Equal("Mozilla/5.0 (iPhone) Safari/604.1", Assert.Single(harness.Auth.UserAgents));
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(HttpMethod.Get, "/session")).StatusCode);
    }

    [Fact]
    public async Task AnAdminPasskeySignIn_OpensTheAdminSession()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();

        var signedIn = await browser.SendAsync(HttpMethod.Post, "/admin/session/passkey", Answer);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.StartsWith(AdminCookie + "=", Assert.Single(signedIn.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedPasskeySignIn_IsPassedOn_AndOpensNothing()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();

        var refused = await browser.SendAsync(HttpMethod.Post, "/session/passkey", new { challengeId = Guid.CreateVersion7(), credential = new { id = "AAAA" } });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("Passkey.SignInFailed", await refused.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.False(refused.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ThePasskeys_AreTheSignedInAccounts_WithTheCsrfToken_ForBothApplications()
    {
        valkey.SkipIfUnavailable();
        await using var harness = await SessionHarness.StartAsync(valkey);
        var customer = harness.NewBrowser();
        var admin = harness.NewBrowser();
        await customer.SignInAsync();
        await admin.SignInAsync(path: "/admin/session/sign-in");

        Assert.Equal(HttpStatusCode.OK, (await customer.SendAsync(HttpMethod.Get, "/session/passkeys")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.SendAsync(HttpMethod.Post, "/session/passkeys/options", new { password = FakeAuthService.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await customer.SendAsync(HttpMethod.Post, "/session/passkeys", Answer)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await customer.SendAsync(HttpMethod.Put, $"/session/passkeys/{Guid.Empty}", new { name = "Phone" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(HttpMethod.Delete, $"/admin/session/passkeys/{Guid.Empty}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.SendAsync(HttpMethod.Delete, $"/session/passkeys/{Guid.Empty}", withCsrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await harness.NewBrowser().SendAsync(HttpMethod.Get, "/session/passkeys")).StatusCode);

        Assert.Equal(
            ["GET /", "POST /options", "POST /", $"PUT /{Guid.Empty}", $"DELETE /{Guid.Empty}"],
            harness.Auth.PasskeyCalls.Select(call => $"{call.Method} {call.Path}"));
        Assert.All(harness.Auth.PasskeyCalls, call => Assert.NotEmpty(call.Bearer));
    }
}
