using System.Net;
using System.Text.Json;
using Gateway.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Gateway.Tests.Sessions;

[Collection(nameof(ValkeyCollection))]
public sealed class SessionTests(ValkeyFixture valkey)
{
    private const string CustomerCookie = "__Host-nbp-session";
    private const string AdminCookie = "__Host-nbp-admin";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SignIn_SetsAHostOnlyHttpOnlyCookie_AndNoTokenReachesTheBrowser()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();

        var response = await browser.SignInAsync();
        var body = await response.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        Assert.StartsWith(CustomerCookie.ToLowerInvariant() + "=", setCookie, StringComparison.Ordinal);
        Assert.Contains("; secure", setCookie, StringComparison.Ordinal);
        Assert.Contains("; httponly", setCookie, StringComparison.Ordinal);
        Assert.Contains("; samesite=lax", setCookie, StringComparison.Ordinal);
        Assert.Contains("; path=/", setCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=", setCookie, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("customer@example.com", json.RootElement.GetProperty("email").GetString());
        Assert.Equal(["catalog:movies:watch"], json.RootElement.GetProperty("permissions").EnumerateArray().Select(item => item.GetString()));
        Assert.False(json.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(json.RootElement.TryGetProperty("refreshToken", out _));
        Assert.DoesNotContain("eyJ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminCookie_LastsOnlyForTheBrowserSession()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);

        var response = await harness.NewBrowser().SignInAsync("admin@example.com", path: "/admin/session/sign-in");

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        Assert.StartsWith(AdminCookie.ToLowerInvariant() + "=", setCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("expires=", setCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedSignIn_PassesTheAuthProblemThrough_WithoutACookie()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);

        var response = await harness.NewBrowser().SignInAsync(password: "wrong password!!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("User.InvalidCredentials", await CodeAsync(response));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ProxiedRequest_CarriesTheBearerToken_AndNoCookie()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();

        var response = await browser.SendAsync(HttpMethod.Get, "/downstream/notes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(harness.Downstream.Received);
        Assert.StartsWith("Bearer eyJ", received.Headers["Authorization"], StringComparison.Ordinal);
        Assert.False(received.Headers.ContainsKey("Cookie"));
    }

    [Fact]
    public async Task RequestWithoutACookie_PassesItsOwnBearerTokenThrough()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);

        await harness.NewBrowser().SendAsync(HttpMethod.Get, "/downstream/notes", bearer: "client-token");

        Assert.Equal("Bearer client-token", Assert.Single(harness.Downstream.Received).Headers["Authorization"]);
    }

    [Fact]
    public async Task ChangingRequest_WithoutTheCsrfToken_Is403_AndNotForwarded()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();

        var missing = await browser.SendAsync(HttpMethod.Post, "/downstream/notes", new { }, withCsrf: false);
        browser.CsrfToken = new string('x', 43);
        var wrong = await browser.SendAsync(HttpMethod.Delete, "/downstream/notes/1");

        foreach (var response in new[] { missing, wrong })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Csrf.Invalid", await CodeAsync(response));
        }

        Assert.Empty(harness.Downstream.Received);
    }

    [Fact]
    public async Task ChangingRequest_WithTheCsrfToken_IsForwardedWithoutIt()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();

        var response = await browser.SendAsync(HttpMethod.Post, "/downstream/notes", new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(Assert.Single(harness.Downstream.Received).Headers.ContainsKey("X-CSRF-Token"));
    }

    [Fact]
    public async Task AdminSession_DoesNotWorkOnCustomerRoutes_AndTheOtherWayRound()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var admin = harness.NewBrowser();
        await admin.SignInAsync("admin@example.com", path: "/admin/session/sign-in");
        var customer = harness.NewBrowser();
        await customer.SignInAsync();

        await admin.SendAsync(HttpMethod.Get, "/downstream/notes");
        await customer.SendAsync(HttpMethod.Get, "/admin/downstream/notes");

        Assert.All(harness.Downstream.Received, received =>
        {
            Assert.False(received.Headers.ContainsKey("Authorization"));
            Assert.False(received.Headers.ContainsKey("Cookie"));
        });

        Assert.Equal("Session.Required", await CodeAsync(await customer.SendAsync(HttpMethod.Get, "/admin/session")));
        Assert.Equal("Session.Required", await CodeAsync(await admin.SendAsync(HttpMethod.Get, "/session")));
    }

    [Fact]
    public async Task ExpiringToken_IsRefreshedOnce_ForConcurrentRequests()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        harness.Auth.SignInAccessTokenLifetime = TimeSpan.FromSeconds(30);
        harness.Auth.RefreshDelay = TimeSpan.FromMilliseconds(300);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => browser.SendAsync(HttpMethod.Get, "/downstream/notes")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, harness.Auth.RefreshCalls);
        Assert.Empty(harness.Auth.Reused);
        Assert.Single(harness.Downstream.Received.Select(received => received.Headers["Authorization"]).Distinct());
    }

    [Fact]
    public async Task TwoGatewayInstances_ShareTheRefresh()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        harness.Auth.SignInAccessTokenLifetime = TimeSpan.FromSeconds(30);
        harness.Auth.RefreshDelay = TimeSpan.FromMilliseconds(300);
        var first = harness.NewBrowser();
        await first.SignInAsync();
        var second = harness.NewBrowser(harness.AddGateway());
        foreach (var (name, value) in first.Cookies)
        {
            second.Cookies[name] = value;
        }

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .SelectMany(_ => new[] { first.SendAsync(HttpMethod.Get, "/downstream/notes"), second.SendAsync(HttpMethod.Get, "/downstream/notes") }));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, harness.Auth.RefreshCalls);
        Assert.Empty(harness.Auth.Reused);
    }

    [Fact]
    public async Task RequestHoldingAStaleSession_DoesNotRefreshAgain()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        harness.Auth.SignInAccessTokenLifetime = TimeSpan.FromSeconds(30);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();
        var id = browser.Cookies[CustomerCookie];
        var sessions = harness.Gateway.Services.GetRequiredService<SessionManager>();
        var stale = new LoadedSession(id, (await harness.Gateway.Services.GetRequiredService<SessionStore>().GetAsync(id))!);

        // The second request read the session before the first one refreshed it.
        var first = await sessions.EnsureFreshAsync(stale);
        var second = await sessions.EnsureFreshAsync(stale);

        Assert.Equal(1, harness.Auth.RefreshCalls);
        Assert.Equal(first.Session!.AccessToken, second.Session!.AccessToken);
    }

    [Fact]
    public async Task RefusedRefresh_EndsTheSession_AndClearsTheCookie()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();
        var cookie = browser.Cookies[CustomerCookie];
        harness.Auth.RefuseRefresh = true;
        harness.Time.Advance(TimeSpan.FromMinutes(15));

        var response = await browser.SendAsync(HttpMethod.Get, "/downstream/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Session.Expired", await CodeAsync(response));
        Assert.False(browser.Cookies.ContainsKey(CustomerCookie));
        Assert.Empty(harness.Downstream.Received);

        browser.Cookies[CustomerCookie] = cookie;
        Assert.Equal("Session.Expired", await CodeAsync(await browser.SendAsync(HttpMethod.Get, "/session")));
    }

    [Fact]
    public async Task SignOut_EndsTheSessionHereAndAtTheAuthService()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();
        var cookie = browser.Cookies[CustomerCookie];

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(HttpMethod.Post, "/session/sign-out", withCsrf: false)).StatusCode);
        var response = await browser.SendAsync(HttpMethod.Post, "/session/sign-out");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(harness.Auth.SignedOut);
        Assert.False(browser.Cookies.ContainsKey(CustomerCookie));

        browser.Cookies[CustomerCookie] = cookie;
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.SendAsync(HttpMethod.Get, "/downstream/notes")).StatusCode);
    }

    [Fact]
    public async Task AdminSession_EndsAfterTheIdleTimeout_ButActivityKeepsItOpen()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var active = harness.NewBrowser();
        await active.SignInAsync("active@example.com", path: "/admin/session/sign-in");
        var idle = harness.NewBrowser();
        await idle.SignInAsync("idle@example.com", path: "/admin/session/sign-in");

        harness.Time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(HttpStatusCode.OK, (await active.SendAsync(HttpMethod.Get, "/admin/session")).StatusCode);
        harness.Time.Advance(TimeSpan.FromMinutes(20));

        Assert.Equal(HttpStatusCode.OK, (await active.SendAsync(HttpMethod.Get, "/admin/session")).StatusCode);
        var expired = await idle.SendAsync(HttpMethod.Get, "/admin/session");
        Assert.Equal("Session.Expired", await CodeAsync(expired));
    }

    [Fact]
    public async Task SignIn_FromAForeignOrigin_Is403()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        browser.Origin = "https://evil.test";

        var response = await browser.SignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Origin.NotAllowed", await CodeAsync(response));

        browser.Origin = GatewayFactory.PublicOrigin;
        Assert.Equal(HttpStatusCode.OK, (await browser.SignInAsync()).StatusCode);
        browser.Origin = GatewayFactory.AdminOrigin;
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SignInAsync()).StatusCode);
    }

    [Fact]
    public async Task CredentialEndpoints_HaveTheirOwnStrictLimit()
    {
        await using var harness = await SessionHarness.StartAsync(valkey, new Dictionary<string, string?>
        {
            ["RateLimiting:Credentials:PermitLimit"] = "2",
            ["RateLimiting:Credentials:Window"] = "00:10:00",
        });
        var browser = harness.NewBrowser();

        await browser.SignInAsync(password: "wrong password!!");
        await browser.SignInAsync(password: "wrong password!!");
        var limited = await browser.SignInAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.SendAsync(HttpMethod.Get, "/session")).StatusCode);
    }

    [Fact]
    public async Task Register_CreatesTheAccountAndSignsItIn()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();

        var created = await browser.SendAsync(HttpMethod.Post, "/session/register", new { email = "new@example.com", password = FakeAuthService.Password });
        var taken = await harness.NewBrowser().SendAsync(HttpMethod.Post, "/session/register", new { email = "taken@example.com", password = FakeAuthService.Password });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(browser.Cookies.ContainsKey(CustomerCookie));
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(HttpMethod.Get, "/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("User.EmailTaken", await CodeAsync(taken));
    }

    [Fact]
    public async Task PasswordChange_ContinuesTheSessionWithTheNewTokens()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync();
        await browser.SendAsync(HttpMethod.Get, "/downstream/notes");

        var wrong = await browser.SendAsync(HttpMethod.Post, "/session/password", new { currentPassword = "nope", newPassword = "another long password" });
        var changed = await browser.SendAsync(HttpMethod.Post, "/session/password", new { currentPassword = FakeAuthService.Password, newPassword = "another long password" });
        await browser.SendAsync(HttpMethod.Get, "/downstream/notes");

        Assert.Equal("User.PasswordIncorrect", await CodeAsync(wrong));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        var tokens = harness.Downstream.Received.Select(received => received.Headers["Authorization"]).ToList();
        Assert.NotEqual(tokens[0], tokens[1]);
        Assert.Equal("Bearer " + harness.Auth.PasswordChanges.First(), tokens[0]);
    }

    [Fact]
    public async Task StoredSession_IsEncrypted_AndNotKeyedByTheCookie()
    {
        await using var harness = await SessionHarness.StartAsync(valkey);
        var browser = harness.NewBrowser();
        await browser.SignInAsync("stored@example.com");
        await browser.SendAsync(HttpMethod.Get, "/downstream/notes");
        var cookie = browser.Cookies[CustomerCookie];
        var accessToken = harness.Downstream.Received.Single().Headers["Authorization"]["Bearer ".Length..];

        var keys = await harness.ValkeyKeysAsync("gateway:session:*");
        var values = await harness.ValkeyValuesAsync("gateway:session:*");

        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => key.ToString().Contains(cookie, StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains(accessToken, StringComparison.Ordinal) || value.Contains("stored@example.com", StringComparison.Ordinal));
        Assert.Equal("stored@example.com", new JsonWebToken(accessToken).GetClaim("email").Value);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
