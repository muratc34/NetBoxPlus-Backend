using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth.API.Sessions;
using Auth.Application.Sessions;
using Auth.Contracts;
using Auth.Domain.UserTokens;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Sessions;

[Collection(nameof(AuthApiCollection))]
public sealed class PasswordResetApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";
    private const string NewPassword = "a brand new passphrase";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Request_ForAnAccount_PublishesAnHourLongToken_AndStoresOnlyItsHash()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var before = DateTimeOffset.UtcNow;

        var response = await RequestAsync(client, email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var requested = Assert.Single(await ResetRequestsAsync(userId));
        Assert.Equal(43, requested.Token.Length);
        Assert.InRange(requested.ExpiresAt, before.AddHours(1), DateTimeOffset.UtcNow.AddHours(1));
        var stored = Assert.Single(await TokensAsync(userId, UserTokenPurpose.PasswordReset));
        Assert.Equal(requested.TokenId, stored.Id);
        Assert.Equal(requested.ExpiresAt, stored.ExpiresAt, TimeSpan.FromMilliseconds(1)); // PostgreSQL keeps microseconds
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(requested.Token)), stored.TokenHash);
    }

    [Fact]
    public async Task Request_AnswersTheSame_ForAnUnknownAddress_ADisabledAccount_AndARepeat()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var disabledEmail = NewEmail();
        var disabledId = await RegisterAsync(client, disabledEmail);
        await UpdateUserAsync(disabledId, disabled: true);

        var first = await RequestAsync(client, email);
        var repeat = await RequestAsync(client, email);
        var unknown = await RequestAsync(client, NewEmail());
        var disabled = await RequestAsync(client, disabledEmail);

        foreach (var response in new[] { first, repeat, unknown, disabled })
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(0, response.Content.Headers.ContentLength);
        }

        Assert.Single(await ResetRequestsAsync(userId));
        Assert.Empty(await ResetRequestsAsync(disabledId));
        Assert.Empty(await TokensAsync(disabledId, UserTokenPurpose.PasswordReset));
    }

    [Fact]
    public async Task Request_WithAMalformedAddress_Returns400()
    {
        using var client = api.CreateClient();

        var response = await RequestAsync(client, "not-an-address");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("User.EmailInvalid", await response.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_AfterAMinute_SendsANewLink_AndTheOldOneStopsWorking()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        await RequestAsync(client, email);
        var old = Assert.Single(await ResetRequestsAsync(userId));
        await UpdateTokensAsync(userId, expiresAt: null, createdAt: DateTimeOffset.UtcNow.AddSeconds(-61));

        await RequestAsync(client, email);

        var fresh = Assert.Single(await ResetRequestsAsync(userId), request => request.TokenId != old.TokenId);
        Assert.NotNull((await TokensAsync(userId, UserTokenPurpose.PasswordReset)).Single(token => token.Id == old.TokenId).RevokedAt);
        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(await ResetAsync(client, old.Token, NewPassword)));
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, fresh.Token, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_SetsThePassword_EndsEverySession_AndTheTokenWorksOnce()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var session = await SignInAsync(client, email, Password);
        var token = await RequestTokenAsync(client, email, userId);

        var reset = await ResetAsync(client, token, NewPassword);
        var again = await ResetAsync(client, token, "yet another passphrase");

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(again));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/refresh", new RefreshRequest(session.RefreshToken), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInRawAsync(client, email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInRawAsync(client, email, NewPassword)).StatusCode);
        Assert.NotNull(Assert.Single(await TokensAsync(userId, UserTokenPurpose.PasswordReset)).UsedAt);
        Assert.True((await LoadUserAsync(userId)).IsEmailVerified);
        var changed = Assert.Single(await ChangesAsync(userId));
        Assert.NotEqual(Guid.Empty, changed.ChangeId);
    }

    [Fact]
    public async Task Reset_LiftsALockout()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var token = await RequestTokenAsync(client, email, userId);
        await UpdateUserAsync(userId, lockedOutUntil: DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInRawAsync(client, email, Password)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, token, NewPassword)).StatusCode);

        var user = await LoadUserAsync(userId);
        Assert.Equal((0, null), (user.FailedSignInCount, user.LockedOutUntil));
        Assert.Equal(HttpStatusCode.OK, (await SignInRawAsync(client, email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_WithAWeakPassword_ReportsThePassword_AndKeepsTheLink()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var token = await RequestTokenAsync(client, email, userId);

        var weak = await ResetAsync(client, token, "short");

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("User.PasswordTooShort", await weak.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.Null(Assert.Single(await TokensAsync(userId, UserTokenPurpose.PasswordReset)).UsedAt);
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, token, NewPassword)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task AnUnknownOrMissingToken_IsTheSameError(string? token)
    {
        using var client = api.CreateClient();

        var response = await ResetAsync(client, token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(response));
    }

    [Fact]
    public async Task AnOverlongToken_IsTheSameError()
    {
        using var client = api.CreateClient();

        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(await ResetAsync(client, new string('a', 257), NewPassword)));
    }

    [Fact]
    public async Task AnExpiredToken_IsRefused_AndThePasswordStays()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var token = await RequestTokenAsync(client, email, userId);
        await UpdateTokensAsync(userId, expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1), createdAt: null);

        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(await ResetAsync(client, token, NewPassword)));
        Assert.Equal(HttpStatusCode.OK, (await SignInRawAsync(client, email, Password)).StatusCode);
        Assert.Empty(await ChangesAsync(userId));
    }

    [Fact]
    public async Task AVerificationToken_CannotReset_AndAResetTokenCannotVerify()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var verification = Assert.Single(await VerificationRequestsAsync(userId)).Token;
        var reset = await RequestTokenAsync(client, email, userId);

        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(await ResetAsync(client, verification, NewPassword)));
        Assert.Equal("EmailVerification.TokenInvalid", await CodeAsync(await client.PostAsJsonAsync("/email-verification/confirm", new ConfirmEmailRequest(reset), Cancellation)));
        Assert.Equal(HttpStatusCode.OK, (await SignInRawAsync(client, email, Password)).StatusCode);
        Assert.False((await LoadUserAsync(userId)).IsEmailVerified);
    }

    [Fact]
    public async Task ADisabledAccount_CannotReset_AndKeepsTheLinkUnused()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var token = await RequestTokenAsync(client, email, userId);
        await UpdateUserAsync(userId, disabled: true);

        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(await ResetAsync(client, token, NewPassword)));
        Assert.Null(Assert.Single(await TokensAsync(userId, UserTokenPurpose.PasswordReset)).UsedAt);
        Assert.Empty(await ChangesAsync(userId));
    }

    [Fact]
    public async Task Reset_EndsTheOtherLinksOfTheAccount()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var old = await RequestTokenAsync(client, email, userId);
        await UpdateTokensAsync(userId, expiresAt: null, createdAt: DateTimeOffset.UtcNow.AddSeconds(-61));
        await RequestAsync(client, email);
        var fresh = (await ResetRequestsAsync(userId)).Single(request => request.Token != old).Token;
        await UnrevokeTokensAsync(userId); // as if two requests had raced and both links were live

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(client, fresh, NewPassword)).StatusCode);

        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(await ResetAsync(client, old, "yet another passphrase")));
        Assert.All(await TokensAsync(userId, UserTokenPurpose.PasswordReset), token => Assert.False(token.UsedAt is null && token.RevokedAt is null));
    }

    [Fact]
    public async Task ConcurrentResets_WithOneToken_SucceedOnce()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var token = await RequestTokenAsync(client, email, userId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => ResetAsync(client, token, $"{NewPassword} {index}")));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.All(
            responses.Where(response => response.StatusCode != HttpStatusCode.NoContent),
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Single(await ChangesAsync(userId));
    }

    [Fact]
    public async Task ChangingThePassword_IsAnnounced_ButSigningInIsNot()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var pair = await SignInAsync(client, email, Password);
        Assert.Empty(await ChangesAsync(userId));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/me/password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(Password, NewPassword)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pair.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request, Cancellation)).StatusCode);

        var changed = Assert.Single(await ChangesAsync(userId));
        Assert.InRange(changed.ChangedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        Assert.False((await LoadUserAsync(userId)).IsEmailVerified);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static async Task<Guid> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/register", new RegisterRequest(email, Password), Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegisterResponse>(Cancellation))!.Id;
    }

    private static Task<HttpResponseMessage> SignInRawAsync(HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/sign-in", new SignInRequest(email, password), Cancellation);

    private static async Task<TokenPair> SignInAsync(HttpClient client, string email, string password)
    {
        var response = await SignInRawAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPair>(Cancellation))!;
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string email)
        => client.PostAsJsonAsync("/password-reset", new RequestPasswordResetRequest(email), Cancellation);

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string? token, string newPassword)
        => client.PostAsJsonAsync("/password-reset/confirm", new ResetPasswordRequest(token, newPassword), Cancellation);

    private async Task<string> RequestTokenAsync(HttpClient client, string email, Guid userId)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync(client, email)).StatusCode);
        return (await ResetRequestsAsync(userId)).OrderByDescending(request => request.OccurredAt).First().Token;
    }

    private async Task<List<PasswordResetRequested>> ResetRequestsAsync(Guid userId)
        => [.. (await OutboxAsync<PasswordResetRequested>()).Where(message => message.UserId == userId)];

    private async Task<List<EmailVerificationRequested>> VerificationRequestsAsync(Guid userId)
        => [.. (await OutboxAsync<EmailVerificationRequested>()).Where(message => message.UserId == userId)];

    private async Task<List<PasswordChanged>> ChangesAsync(Guid userId)
        => [.. (await OutboxAsync<PasswordChanged>()).Where(message => message.UserId == userId)];

    private async Task<List<T>> OutboxAsync<T>()
    {
        await using var scope = api.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().OutboxMessages
            .Where(message => message.Type == IntegrationEvents.MessageTypeOf(typeof(T)))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<T>(payload, IntegrationEvents.SerializerOptions)!)];
    }

    private async Task<List<UserToken>> TokensAsync(Guid userId, UserTokenPurpose purpose)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens.AsNoTracking()
            .Where(token => token.UserId == userId && token.Purpose == purpose)
            .ToListAsync(Cancellation);
    }

    /// <summary>Moves the user's reset tokens in time, as if they had been issued earlier.</summary>
    private async Task UpdateTokensAsync(Guid userId, DateTimeOffset? expiresAt, DateTimeOffset? createdAt)
    {
        await using var scope = api.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens
            .Where(token => token.UserId == userId && token.Purpose == UserTokenPurpose.PasswordReset);
        if (expiresAt is { } expires)
        {
            await tokens.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ExpiresAt, expires), Cancellation);
        }

        if (createdAt is { } created)
        {
            await tokens.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.CreatedAt, created), Cancellation);
        }
    }

    private async Task UnrevokeTokensAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens
            .Where(token => token.UserId == userId && token.Purpose == UserTokenPurpose.PasswordReset)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, (DateTimeOffset?)null), Cancellation);
    }

    private async Task UpdateUserAsync(Guid userId, bool? disabled = null, DateTimeOffset? lockedOutUntil = null)
    {
        await using var scope = api.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.Where(user => user.Id == userId);
        if (disabled is { } isDisabled)
        {
            await users.ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsDisabled, isDisabled), Cancellation);
        }

        if (lockedOutUntil is { } until)
        {
            await users.ExecuteUpdateAsync(
                setters => setters.SetProperty(user => user.LockedOutUntil, until).SetProperty(user => user.FailedSignInCount, 5),
                Cancellation);
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
