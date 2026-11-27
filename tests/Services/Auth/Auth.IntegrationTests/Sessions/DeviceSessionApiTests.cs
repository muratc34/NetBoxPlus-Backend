using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.API.Sessions;
using Auth.Application.Sessions;
using Auth.Application.Sessions.Devices;
using Auth.Domain.RefreshTokens;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Auth.IntegrationTests.Sessions;

/// <summary>The account's list of sessions (devices), and ending them.</summary>
[Collection(nameof(AuthApiCollection))]
public sealed class DeviceSessionApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";
    private const string WindowsChrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";
    private const string IPhoneSafari = "Mozilla/5.0 (iPhone; CPU iPhone OS 19_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/19.1 Mobile/15E148 Safari/604.1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sessions_AreListedPerSignIn_NamedAfterTheDevice_TheCallersMarked()
    {
        using var client = api.CreateClient();
        var email = await RegisterAsync(client, WindowsChrome);
        var phone = await SignInAsync(client, email, IPhoneSafari);
        var laptop = await SignInAsync(client, email, WindowsChrome);

        // A refresh renews the session; it is still one entry, and the token carries the same id.
        var renewed = await RefreshAsync(client, phone.RefreshToken);
        Assert.Equal(phone.SessionId, renewed.SessionId);
        Assert.Equal(phone.SessionId.ToString(), new JsonWebToken(renewed.AccessToken).GetClaim("sid").Value);

        var sessions = await SessionsAsync(client, laptop.AccessToken);

        Assert.Equal(3, sessions.Count);
        Assert.Equal(phone.SessionId, sessions[0].Id);
        Assert.Equal("Safari · iOS", sessions[0].DeviceLabel);
        Assert.True(sessions[0].LastActiveAt > sessions[0].SignedInAt);
        var mine = Assert.Single(sessions, session => session.Current);
        Assert.Equal((laptop.SessionId, "Chrome · Windows", SessionKind.Customer), (mine.Id, mine.DeviceLabel, mine.Kind));
    }

    [Fact]
    public async Task AnEndedSession_CannotBeRenewed_AndLeavesTheList()
    {
        using var client = api.CreateClient();
        var email = await RegisterAsync(client, WindowsChrome);
        var phone = await SignInAsync(client, email, IPhoneSafari);
        var laptop = await SignInAsync(client, email, WindowsChrome);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/me/sessions/{phone.SessionId}", laptop.AccessToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/refresh", new RefreshRequest(phone.RefreshToken), Cancellation)).StatusCode);
        Assert.DoesNotContain(await SessionsAsync(client, laptop.AccessToken), session => session.Id == phone.SessionId);
        var again = await SendAsync(client, HttpMethod.Delete, $"/me/sessions/{phone.SessionId}", laptop.AccessToken);
        Assert.Equal((HttpStatusCode.NotFound, "Session.NotFound"), (again.StatusCode, await CodeAsync(again)));
    }

    [Fact]
    public async Task AnotherAccountsSession_IsNotFound_AndStaysOpen()
    {
        using var client = api.CreateClient();
        var mine = await SignInAsync(client, await RegisterAsync(client, WindowsChrome), WindowsChrome);
        var theirs = await SignInAsync(client, await RegisterAsync(client, WindowsChrome), IPhoneSafari);

        var response = await SendAsync(client, HttpMethod.Delete, $"/me/sessions/{theirs.SessionId}", mine.AccessToken);

        Assert.Equal((HttpStatusCode.NotFound, "Session.NotFound"), (response.StatusCode, await CodeAsync(response)));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/refresh", new RefreshRequest(theirs.RefreshToken), Cancellation)).StatusCode);
    }

    [Fact]
    public async Task EndingTheOthers_KeepsTheCallers_AndSaysWhichEnded()
    {
        using var client = api.CreateClient();
        var email = await RegisterAsync(client, WindowsChrome);
        var phone = await SignInAsync(client, email, IPhoneSafari);
        var laptop = await SignInAsync(client, email, WindowsChrome);

        var response = await SendAsync(client, HttpMethod.Delete, "/me/sessions", laptop.AccessToken);
        var ended = await response.Content.ReadFromJsonAsync<EndedSessions>(Json, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, ended!.Ids.Count);
        Assert.Contains(phone.SessionId, ended.Ids);
        Assert.DoesNotContain(laptop.SessionId, ended.Ids);
        Assert.Equal([laptop.SessionId], (await SessionsAsync(client, laptop.AccessToken)).Select(session => session.Id));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/refresh", new RefreshRequest(laptop.RefreshToken), Cancellation)).StatusCode);
    }

    [Fact]
    public async Task WithoutAToken_TheSessionsAreNotShown()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/me/sessions", Cancellation)).StatusCode);

    private static async Task<string> RegisterAsync(HttpClient client, string userAgent)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/register") { Content = JsonContent.Create(new RegisterRequest(email, Password)) };
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request, Cancellation)).StatusCode);
        return email;
    }

    private static async Task<TokenPair> SignInAsync(HttpClient client, string email, string userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sign-in") { Content = JsonContent.Create(new SignInRequest(email, Password)) };
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPair>(Json, Cancellation))!;
    }

    private static async Task<TokenPair> RefreshAsync(HttpClient client, string refreshToken)
    {
        // A moment later than the sign-in, so the renewal is the newer activity.
        await Task.Delay(20, Cancellation);
        var response = await client.PostAsJsonAsync("/refresh", new RefreshRequest(refreshToken), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPair>(Json, Cancellation))!;
    }

    private static async Task<List<DeviceSession>> SessionsAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/me/sessions", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<DeviceSession>>(Json, Cancellation))!;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request, Cancellation);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString();
}
