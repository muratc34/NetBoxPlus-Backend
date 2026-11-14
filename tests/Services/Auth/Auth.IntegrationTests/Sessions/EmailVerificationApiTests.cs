using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth.API.Sessions;
using Auth.Application.EmailVerification.GetVerificationStatus;
using Auth.Application.Sessions;
using Auth.Application.Users.GetMe;
using Auth.Contracts;
using Auth.Domain.UserTokens;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Sessions;

[Collection(nameof(AuthApiCollection))]
public sealed class EmailVerificationApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_PublishesAVerificationToken_AndStoresOnlyItsHash()
    {
        using var client = api.CreateClient();
        var before = DateTimeOffset.UtcNow;

        var userId = await RegisterAsync(client, NewEmail());

        var requested = Assert.Single(await RequestsAsync(userId));
        Assert.Equal(43, requested.Token.Length);
        Assert.InRange(requested.ExpiresAt, before.AddHours(24), DateTimeOffset.UtcNow.AddHours(24));

        var stored = Assert.Single(await TokensAsync(userId));
        Assert.Equal((requested.TokenId, UserTokenPurpose.EmailVerification), (stored.Id, stored.Purpose));
        Assert.Equal(requested.ExpiresAt, stored.ExpiresAt, TimeSpan.FromMilliseconds(1)); // PostgreSQL keeps microseconds
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(requested.Token)), stored.TokenHash);
        Assert.False((await LoadUserAsync(userId)).IsEmailVerified);
    }

    [Fact]
    public async Task Confirm_VerifiesTheAddress_AndTheTokenWorksOnce()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var token = Assert.Single(await RequestsAsync(userId)).Token;
        var accessToken = await SignInAsync(client, email);
        Assert.Equal(new VerificationStatus(false, null), await GetStatusAsync(client, accessToken));

        var first = await ConfirmAsync(client, token);
        var again = await ConfirmAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("EmailVerification.TokenInvalid", await CodeAsync(again));
        var me = await GetMeAsync(client, accessToken);
        Assert.True(me.EmailVerified);
        var status = await GetStatusAsync(client, accessToken);
        Assert.True(status.Verified);
        Assert.NotNull(status.VerifiedAt);
        Assert.NotNull(Assert.Single(await TokensAsync(userId)).UsedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task AnUnknownOrMissingToken_IsTheSameError(string? token)
    {
        using var client = api.CreateClient();

        var response = await ConfirmAsync(client, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("EmailVerification.TokenInvalid", await CodeAsync(response));
    }

    [Fact]
    public async Task AnOverlongToken_IsTheSameError()
    {
        using var client = api.CreateClient();

        var response = await ConfirmAsync(client, new string('a', 257));

        Assert.Equal("EmailVerification.TokenInvalid", await CodeAsync(response));
    }

    [Fact]
    public async Task AnExpiredToken_IsRefused_AndTheAddressStaysUnverified()
    {
        using var client = api.CreateClient();
        var userId = await RegisterAsync(client, NewEmail());
        var token = Assert.Single(await RequestsAsync(userId)).Token;
        await UpdateTokensAsync(userId, DateTimeOffset.UtcNow.AddSeconds(-1), createdAt: null);

        var response = await ConfirmAsync(client, token);

        Assert.Equal("EmailVerification.TokenInvalid", await CodeAsync(response));
        Assert.False((await LoadUserAsync(userId)).IsEmailVerified);
        Assert.Null(Assert.Single(await TokensAsync(userId)).UsedAt);
    }

    [Fact]
    public async Task ConcurrentConfirmations_WithOneToken_SucceedOnce()
    {
        using var client = api.CreateClient();
        var userId = await RegisterAsync(client, NewEmail());
        var token = Assert.Single(await RequestsAsync(userId)).Token;

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ConfirmAsync(client, token)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.All(
            responses.Where(response => response.StatusCode != HttpStatusCode.NoContent),
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.True((await LoadUserAsync(userId)).IsEmailVerified);
    }

    [Fact]
    public async Task Resend_RightAfterTheLastLink_IsRefused()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);

        var response = await ResendAsync(client, await SignInAsync(client, email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("EmailVerification.RequestedRecently", await CodeAsync(response));
        Assert.Single(await RequestsAsync(userId));
    }

    [Fact]
    public async Task Resend_AfterAMinute_SendsANewLink_AndTheOldOneStopsWorking()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var old = Assert.Single(await RequestsAsync(userId));
        await UpdateTokensAsync(userId, expiresAt: null, createdAt: DateTimeOffset.UtcNow.AddSeconds(-61));
        var accessToken = await SignInAsync(client, email);

        var response = await ResendAsync(client, accessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var fresh = Assert.Single(await RequestsAsync(userId), request => request.TokenId != old.TokenId);
        Assert.NotEqual(old.Token, fresh.Token);
        Assert.NotNull((await TokensAsync(userId)).Single(token => token.Id == old.TokenId).RevokedAt);
        Assert.Equal("EmailVerification.TokenInvalid", await CodeAsync(await ConfirmAsync(client, old.Token)));
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(client, fresh.Token)).StatusCode);
    }

    [Fact]
    public async Task Resend_ForAVerifiedAddress_IsRefused()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        await ConfirmAsync(client, Assert.Single(await RequestsAsync(userId)).Token);
        await UpdateTokensAsync(userId, expiresAt: null, createdAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var response = await ResendAsync(client, await SignInAsync(client, email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("EmailVerification.AlreadyVerified", await CodeAsync(response));
    }

    [Fact]
    public async Task ResendAndStatus_NeedASignIn()
    {
        using var client = api.CreateClient();

        var response = await client.PostAsync("/email-verification", content: null, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/email-verification", Cancellation)).StatusCode);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static async Task<Guid> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/register", new RegisterRequest(email, Password), Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegisterResponse>(Cancellation))!.Id;
    }

    private static async Task<string> SignInAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, Password), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPair>(Cancellation))!.AccessToken;
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string? token)
        => client.PostAsJsonAsync("/email-verification/confirm", new ConfirmEmailRequest(token), Cancellation);

    private static Task<HttpResponseMessage> ResendAsync(HttpClient client, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/email-verification");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request, Cancellation);
    }

    private static async Task<VerificationStatus> GetStatusAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/email-verification");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VerificationStatus>(Cancellation))!;
    }

    private static async Task<MeResponse> GetMeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MeResponse>(Cancellation))!;
    }

    private async Task<List<EmailVerificationRequested>> RequestsAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().OutboxMessages
            .Where(message => message.Type == IntegrationEvents.MessageTypeOf(typeof(EmailVerificationRequested)))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        return [.. payloads
            .Select(payload => JsonSerializer.Deserialize<EmailVerificationRequested>(payload, IntegrationEvents.SerializerOptions)!)
            .Where(message => message.UserId == userId)];
    }

    private async Task<List<UserToken>> TokensAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens.AsNoTracking()
            .Where(token => token.UserId == userId)
            .ToListAsync(Cancellation);
    }

    /// <summary>Moves the user's tokens in time, as if they had been issued earlier.</summary>
    private async Task UpdateTokensAsync(Guid userId, DateTimeOffset? expiresAt, DateTimeOffset? createdAt)
    {
        await using var scope = api.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens.Where(token => token.UserId == userId);
        if (expiresAt is { } expires)
        {
            await tokens.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ExpiresAt, expires), Cancellation);
        }

        if (createdAt is { } created)
        {
            await tokens.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.CreatedAt, created), Cancellation);
        }
    }

    private async Task<Auth.Domain.Users.User> LoadUserAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId, Cancellation);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString();
}
