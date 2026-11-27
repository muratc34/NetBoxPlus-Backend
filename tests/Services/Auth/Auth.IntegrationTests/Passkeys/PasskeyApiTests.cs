using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.API.Sessions;
using Auth.Application.Passkeys;
using Auth.Application.Sessions;
using Auth.Contracts;
using Auth.Domain.Passkeys;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Auth.IntegrationTests.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Passkeys;

/// <summary>Adding passkeys and signing in with them, against real signatures from <see cref="TestAuthenticator"/>.</summary>
[Collection(nameof(AuthApiCollection))]
public sealed class PasskeyApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APasskey_IsAdded_Listed_AndSignsTheUserIn_WithoutAPassword()
    {
        using var client = api.CreateClient();
        var (userId, tokens) = await AccountAsync(client);
        using var authenticator = new TestAuthenticator();

        var created = await AddPasskeyAsync(client, tokens.AccessToken, authenticator, "My phone");

        var listed = Assert.Single(await ListAsync(client, tokens.AccessToken));
        Assert.Equal((created, "My phone", (DateTimeOffset?)null), (listed.Id, listed.Name, listed.LastUsedAt));
        Assert.Equal(userId, (await PublishedAsync<PasskeyAdded>(message => message.PasskeyId == created)).UserId);

        var signedIn = await SignInAsync(client, authenticator);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        var pair = (await signedIn.Content.ReadFromJsonAsync<TokenPair>(Json, Cancellation))!;
        Assert.Equal(userId.ToString(), new JsonWebToken(pair.AccessToken).Subject);
        Assert.NotNull(Assert.Single(await ListAsync(client, pair.AccessToken)).LastUsedAt);
    }

    [Fact]
    public async Task AddingAPasskey_AsksForThePasswordAgain()
    {
        using var client = api.CreateClient();
        var (_, tokens) = await AccountAsync(client);

        var wrong = await SendAsync(client, HttpMethod.Post, "/me/passkeys/options", tokens.AccessToken, new PasskeyOptionsRequest("wrong password"));

        Assert.Equal((HttpStatusCode.BadRequest, "User.PasswordIncorrect"), (wrong.StatusCode, await CodeAsync(wrong)));
    }

    [Fact]
    public async Task AnAnswerFromAnotherOrigin_OrToUsedOptions_IsRefused()
    {
        using var client = api.CreateClient();
        var (_, tokens) = await AccountAsync(client);
        using var authenticator = new TestAuthenticator();

        var start = await StartRegistrationAsync(client, tokens.AccessToken);
        var phished = await SendAsync(client, HttpMethod.Post, "/me/passkeys", tokens.AccessToken,
            new AddPasskeyRequest(start.ChallengeId, "Phone", authenticator.Create(start.Options, origin: "https://netboxplus.example.evil")));
        var again = await SendAsync(client, HttpMethod.Post, "/me/passkeys", tokens.AccessToken,
            new AddPasskeyRequest(start.ChallengeId, "Phone", authenticator.Create(start.Options)));

        Assert.Equal((HttpStatusCode.BadRequest, "Passkey.Invalid"), (phished.StatusCode, await CodeAsync(phished)));
        Assert.Equal((HttpStatusCode.BadRequest, "Passkey.ChallengeInvalid"), (again.StatusCode, await CodeAsync(again)));
        Assert.Empty(await ListAsync(client, tokens.AccessToken));
    }

    [Fact]
    public async Task APasskeySignIn_IsAnnouncedAsASignIn()
    {
        using var client = api.CreateClient();
        var (userId, tokens) = await AccountAsync(client);
        using var authenticator = new TestAuthenticator();
        await AddPasskeyAsync(client, tokens.AccessToken, authenticator, "Key");

        var signedIn = await (await SignInAsync(client, authenticator)).Content.ReadFromJsonAsync<TokenPair>(Cancellation);

        var announced = await PublishedAsync<SignedIn>(item => item.SessionId == signedIn!.SessionId);
        Assert.Equal((userId, false, "Customer"), (announced.UserId, announced.Registration, announced.Kind));
    }

    [Fact]
    public async Task ASignature_WorksOnce_AndACounterThatGoesBack_IsRefused()
    {
        using var client = api.CreateClient();
        var (_, tokens) = await AccountAsync(client);
        using var authenticator = new TestAuthenticator { Counter = 5 };
        await AddPasskeyAsync(client, tokens.AccessToken, authenticator, "Key");

        // A signature answers one challenge: sent again with new options it fails.
        var options = await StartSignInAsync(client);
        var answer = authenticator.Sign(options.Options);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/sign-in/passkey", new PasskeySignInRequest(options.ChallengeId, answer), Cancellation)).StatusCode);
        var replayed = await client.PostAsJsonAsync("/sign-in/passkey", new PasskeySignInRequest(options.ChallengeId, answer), Cancellation);
        var resent = await client.PostAsJsonAsync("/sign-in/passkey", new PasskeySignInRequest((await StartSignInAsync(client)).ChallengeId, answer), Cancellation);

        authenticator.Counter = 4;
        var copied = await SignInAsync(client, authenticator);

        authenticator.Counter = 9;
        var counted = await SignInAsync(client, authenticator);

        Assert.Equal((HttpStatusCode.Unauthorized, "Passkey.SignInFailed"), (replayed.StatusCode, await CodeAsync(replayed)));
        Assert.Equal(HttpStatusCode.Unauthorized, resent.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, copied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
    }

    [Fact]
    public async Task AnAdminSession_NeedsThePermission_LikeThePasswordOne()
    {
        using var client = api.CreateClient();
        var (userId, tokens) = await AccountAsync(client);
        using var authenticator = new TestAuthenticator();
        await AddPasskeyAsync(client, tokens.AccessToken, authenticator, "Key");

        var refused = await SignInAsync(client, authenticator, admin: true);
        await GrantAsync(userId, Permissions.Auth.AdminSignIn);
        var admitted = await SignInAsync(client, authenticator, admin: true);

        Assert.Equal((HttpStatusCode.Unauthorized, "Passkey.SignInFailed"), (refused.StatusCode, await CodeAsync(refused)));
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
    }

    [Fact]
    public async Task ARemovedPasskey_OrADisabledAccount_SignsNoOneIn()
    {
        using var client = api.CreateClient();
        var (userId, tokens) = await AccountAsync(client);
        using var removed = new TestAuthenticator();
        using var kept = new TestAuthenticator();
        var removedId = await AddPasskeyAsync(client, tokens.AccessToken, removed, "Old");
        await AddPasskeyAsync(client, tokens.AccessToken, kept, "New");

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Put, $"/me/passkeys/{removedId}", tokens.AccessToken, new RenamePasskeyRequest(" Older "))).StatusCode);
        Assert.Contains(await ListAsync(client, tokens.AccessToken), passkey => passkey.Name == "Older");
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/me/passkeys/{removedId}", tokens.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Delete, $"/me/passkeys/{removedId}", tokens.AccessToken)).StatusCode);
        Assert.Equal(userId, (await PublishedAsync<PasskeyRemoved>(message => message.PasskeyId == removedId)).UserId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, removed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(client, kept)).StatusCode);

        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var user = await context.Users.SingleAsync(user => user.Id == userId, Cancellation);
            user.Disable();
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(client, kept)).StatusCode);
    }

    [Fact]
    public async Task AnotherAccountsPasskey_IsNotTheirsToChange()
    {
        using var client = api.CreateClient();
        var (_, owner) = await AccountAsync(client);
        var (_, stranger) = await AccountAsync(client);
        using var authenticator = new TestAuthenticator();
        var id = await AddPasskeyAsync(client, owner.AccessToken, authenticator, "Key");

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Delete, $"/me/passkeys/{id}", stranger.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Put, $"/me/passkeys/{id}", stranger.AccessToken, new RenamePasskeyRequest("Mine"))).StatusCode);
        Assert.Empty(await ListAsync(client, stranger.AccessToken));
        Assert.Single(await ListAsync(client, owner.AccessToken));
    }

    [Fact]
    public async Task AnotherAccountsOptions_CannotAddAPasskeyHere()
    {
        using var client = api.CreateClient();
        var (_, owner) = await AccountAsync(client);
        var (_, stranger) = await AccountAsync(client);
        using var authenticator = new TestAuthenticator();

        var ownersOptions = await StartRegistrationAsync(client, owner.AccessToken);
        var taken = await SendAsync(client, HttpMethod.Post, "/me/passkeys", stranger.AccessToken,
            new AddPasskeyRequest(ownersOptions.ChallengeId, "Key", authenticator.Create(ownersOptions.Options)));

        Assert.Equal((HttpStatusCode.BadRequest, "Passkey.ChallengeInvalid"), (taken.StatusCode, await CodeAsync(taken)));
        Assert.Empty(await ListAsync(client, stranger.AccessToken));
    }

    [Fact]
    public async Task AnAccountKeepsAtMostTen_AndLosesThemWithTheAccount()
    {
        using var client = api.CreateClient();
        var (userId, tokens) = await AccountAsync(client);
        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            for (var i = 0; i < UserPasskey.MaxPerUser; i++)
            {
                context.Passkeys.Add(UserPasskey.Register(userId, Guid.NewGuid().ToByteArray(), [1, 2, 3], 0, $"Key {i}", DateTimeOffset.UtcNow).Value);
            }

            await context.SaveChangesAsync(Cancellation);
        }

        var full = await SendAsync(client, HttpMethod.Post, "/me/passkeys/options", tokens.AccessToken, new PasskeyOptionsRequest(Password));
        Assert.Equal((HttpStatusCode.Conflict, "Passkey.TooMany"), (full.StatusCode, await CodeAsync(full)));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/me/deletion", tokens.AccessToken, new { password = Password })).StatusCode);
        await using (var scope = api.CreateScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Passkeys.AnyAsync(passkey => passkey.UserId == userId, Cancellation));
        }
    }

    [Fact]
    public async Task AMalformedAnswer_IsARefusal_NotAnError()
    {
        using var client = api.CreateClient();
        var options = await StartSignInAsync(client);
        using var garbage = JsonDocument.Parse("""{ "id": "AAAA", "rawId": "AAAA", "type": "public-key", "response": { "clientDataJSON": "e30" } }""");

        var response = await client.PostAsJsonAsync("/sign-in/passkey", new PasskeySignInRequest(options.ChallengeId, garbage.RootElement), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<(Guid UserId, TokenPair Tokens)> AccountAsync(HttpClient client)
    {
        var email = $"passkey-{Guid.NewGuid():N}@example.com";
        var registered = await client.PostAsJsonAsync("/register", new RegisterRequest(email, Password), Cancellation);
        var account = (await registered.Content.ReadFromJsonAsync<RegisterResponse>(Json, Cancellation))!;
        return (account.Id, new TokenPair(account.AccessToken, account.AccessTokenExpiresAt, account.RefreshToken, account.RefreshTokenExpiresAt, account.SessionId));
    }

    private static async Task<PasskeyCeremonyStart> StartRegistrationAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/me/passkeys/options", accessToken, new PasskeyOptionsRequest(Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PasskeyCeremonyStart>(Json, Cancellation))!;
    }

    private static async Task<Guid> AddPasskeyAsync(HttpClient client, string accessToken, TestAuthenticator authenticator, string name)
    {
        var start = await StartRegistrationAsync(client, accessToken);
        var response = await SendAsync(client, HttpMethod.Post, "/me/passkeys", accessToken, new AddPasskeyRequest(start.ChallengeId, name, authenticator.Create(start.Options)));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancellation));
        return (await response.Content.ReadFromJsonAsync<CreatedPasskey>(Json, Cancellation))!.Id;
    }

    private static async Task<PasskeyCeremonyStart> StartSignInAsync(HttpClient client)
    {
        var response = await client.PostAsync("/sign-in/passkey/options", null, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PasskeyCeremonyStart>(Json, Cancellation))!;
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, TestAuthenticator authenticator, bool admin = false)
    {
        var options = await StartSignInAsync(client);
        return await client.PostAsJsonAsync(admin ? "/sign-in/passkey/admin" : "/sign-in/passkey", new PasskeySignInRequest(options.ChallengeId, authenticator.Sign(options.Options)), Cancellation);
    }

    private static async Task<List<PasskeySummary>> ListAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/me/passkeys", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<PasskeySummary>>(Json, Cancellation))!;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request, Cancellation);
    }

    private async Task GrantAsync(Guid userId, params string[] permissions)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var role = Role.Create($"Role {Guid.NewGuid():N}", permissions).Value;
        var user = await context.Users.Include(user => user.Roles).SingleAsync(user => user.Id == userId, Cancellation);
        context.Roles.Add(role);
        user.AssignRole(role.Id);
        await context.SaveChangesAsync(Cancellation);
    }

    private async Task<T> PublishedAsync<T>(Func<T, bool> match)
    {
        await using var scope = api.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().OutboxMessages
            .Where(message => message.Type == IntegrationEvents.MessageTypeOf(typeof(T)))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        return payloads.Select(payload => JsonSerializer.Deserialize<T>(payload, IntegrationEvents.SerializerOptions)!).Single(match);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString();
}
