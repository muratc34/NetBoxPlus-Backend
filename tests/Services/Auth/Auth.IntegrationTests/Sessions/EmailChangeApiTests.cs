using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.API.Sessions;
using Auth.Application.EmailVerification.GetVerificationStatus;
using Auth.Application.Sessions;
using Auth.Contracts;
using Auth.Domain.UserTokens;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Sessions;

[Collection(nameof(AuthApiCollection))]
public sealed class EmailChangeApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequest_SendsALinkToTheNewAddress_AndChangesNothingYet()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var accessToken = await SignInAsync(client, email);
        var newEmail = NewEmail();

        var response = await RequestAsync(client, accessToken, Password, "  " + newEmail.ToUpperInvariant() + " ");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var requested = Assert.Single(await PublishedAsync<EmailChangeRequested>(userId));
        Assert.Equal(newEmail, requested.NewEmail);
        Assert.Equal(43, requested.Token.Length);
        var stored = Assert.Single(await TokensAsync(userId), token => token.Purpose == UserTokenPurpose.EmailChange);
        Assert.Equal((requested.TokenId, newEmail), (stored.Id, stored.Email));
        var user = await LoadUserAsync(userId);
        Assert.Equal((email, 1), (user.Email.Value, user.EmailRevision));
        Assert.Empty(await PublishedAsync<EmailChanged>(userId));
    }

    [Fact]
    public async Task TheLink_MovesTheAccount_VerifiesTheNewAddress_AndAnnouncesIt()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var accessToken = await SignInAsync(client, email);
        var newEmail = NewEmail();
        await RequestAsync(client, accessToken, Password, newEmail);
        var token = Assert.Single(await PublishedAsync<EmailChangeRequested>(userId)).Token;

        var first = await ConfirmAsync(client, token);
        var again = await ConfirmAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("EmailChange.TokenInvalid", await CodeAsync(again));
        var user = await LoadUserAsync(userId);
        Assert.Equal((newEmail, 2, true), (user.Email.Value, user.EmailRevision, user.IsEmailVerified));
        var changed = Assert.Single(await PublishedAsync<EmailChanged>(userId));
        Assert.Equal((newEmail, 2), (changed.Email, changed.Revision));
        // The first verification of the account, through the new address.
        Assert.Equal(newEmail, Assert.Single(await PublishedAsync<EmailVerified>(userId)).Email);
        // A token issued before the change still names the old address; the status names the new one.
        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/email-verification");
        statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var status = await client.SendAsync(statusRequest, Cancellation);
        Assert.Equal(newEmail, (await status.Content.ReadFromJsonAsync<VerificationStatus>(Cancellation))!.Email);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/sign-in", new SignInRequest(newEmail, Password), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, Password), Cancellation)).StatusCode);
    }

    [Fact]
    public async Task AChange_RevokesTheLinksSentToTheOldAddress()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var accessToken = await SignInAsync(client, email);
        var verificationToken = Assert.Single(await PublishedAsync<EmailVerificationRequested>(userId)).Token;
        await client.PostAsJsonAsync("/password-reset", new RequestPasswordResetRequest(email), Cancellation);
        var resetToken = Assert.Single(await PublishedAsync<PasswordResetRequested>(userId)).Token;
        await RequestAsync(client, accessToken, Password, NewEmail());

        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(client, Assert.Single(await PublishedAsync<EmailChangeRequested>(userId)).Token)).StatusCode);

        var verify = await client.PostAsJsonAsync("/email-verification/confirm", new ConfirmEmailRequest(verificationToken), Cancellation);
        var reset = await client.PostAsJsonAsync("/password-reset/confirm", new ResetPasswordRequest(resetToken, "another correct horse battery"), Cancellation);
        Assert.Equal("EmailVerification.TokenInvalid", await CodeAsync(verify));
        Assert.Equal("PasswordReset.TokenInvalid", await CodeAsync(reset));
        Assert.All(await TokensAsync(userId), token => Assert.True(token.UsedAt is not null || token.RevokedAt is not null));
    }

    [Fact]
    public async Task AWrongPassword_IsRefused_AndCountsAsAFailedSignIn()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var accessToken = await SignInAsync(client, email);

        var response = await RequestAsync(client, accessToken, "not the password at all", NewEmail());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("User.PasswordIncorrect", await CodeAsync(response));
        Assert.Equal(1, (await LoadUserAsync(userId)).FailedSignInCount);
        Assert.Empty(await PublishedAsync<EmailChangeRequested>(userId));
    }

    [Fact]
    public async Task AnAddressInUse_OrTheCurrentOne_IsRefused()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var other = NewEmail();
        var userId = await RegisterAsync(client, email);
        await RegisterAsync(client, other);
        var accessToken = await SignInAsync(client, email);

        var taken = await RequestAsync(client, accessToken, Password, other);
        var same = await RequestAsync(client, accessToken, Password, email.ToUpperInvariant());
        var invalid = await RequestAsync(client, accessToken, Password, "not-an-address");

        Assert.Equal((HttpStatusCode.Conflict, "User.EmailTaken"), (taken.StatusCode, await CodeAsync(taken)));
        Assert.Equal((HttpStatusCode.BadRequest, "User.EmailUnchanged"), (same.StatusCode, await CodeAsync(same)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(await PublishedAsync<EmailChangeRequested>(userId));
    }

    [Fact]
    public async Task ANewRequest_WaitsAMinute_AndReplacesTheEarlierLink()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var accessToken = await SignInAsync(client, email);
        await RequestAsync(client, accessToken, Password, NewEmail());

        var tooSoon = await RequestAsync(client, accessToken, Password, NewEmail());
        Assert.Equal((HttpStatusCode.Conflict, "EmailChange.RequestedRecently"), (tooSoon.StatusCode, await CodeAsync(tooSoon)));

        await MoveTokensBackAsync(userId, TimeSpan.FromMinutes(2));
        var latest = NewEmail();
        Assert.Equal(HttpStatusCode.NoContent, (await RequestAsync(client, accessToken, Password, latest)).StatusCode);

        var requests = await PublishedAsync<EmailChangeRequested>(userId);
        Assert.Equal(2, requests.Count);
        var earlier = requests.Single(request => request.NewEmail != latest);
        Assert.Equal("EmailChange.TokenInvalid", await CodeAsync(await ConfirmAsync(client, earlier.Token)));
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(client, requests.Single(request => request.NewEmail == latest).Token)).StatusCode);
        Assert.Equal(latest, (await LoadUserAsync(userId)).Email.Value);
    }

    [Fact]
    public async Task AnAddressTakenSinceTheLinkWasSent_IsAConflict()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var newEmail = NewEmail();
        var userId = await RegisterAsync(client, email);
        var accessToken = await SignInAsync(client, email);
        await RequestAsync(client, accessToken, Password, newEmail);
        await RegisterAsync(client, newEmail);

        var response = await ConfirmAsync(client, Assert.Single(await PublishedAsync<EmailChangeRequested>(userId)).Token);

        Assert.Equal((HttpStatusCode.Conflict, "User.EmailTaken"), (response.StatusCode, await CodeAsync(response)));
        Assert.Equal(email, (await LoadUserAsync(userId)).Email.Value);
        Assert.Empty(await PublishedAsync<EmailChanged>(userId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task AnUnknownOrMissingToken_IsTheSameError(string? token)
    {
        using var client = api.CreateClient();

        var response = await ConfirmAsync(client, token);

        Assert.Equal((HttpStatusCode.BadRequest, "EmailChange.TokenInvalid"), (response.StatusCode, await CodeAsync(response)));
    }

    [Fact]
    public async Task AnotherPurposesToken_DoesNotChangeTheAddress()
    {
        using var client = api.CreateClient();
        var userId = await RegisterAsync(client, NewEmail());
        var verificationToken = Assert.Single(await PublishedAsync<EmailVerificationRequested>(userId)).Token;

        var response = await ConfirmAsync(client, verificationToken);

        Assert.Equal("EmailChange.TokenInvalid", await CodeAsync(response));
    }

    [Fact]
    public async Task AnExpiredLink_OrADisabledAccount_IsTheSameError()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var accessToken = await SignInAsync(client, email);
        await RequestAsync(client, accessToken, Password, NewEmail());
        var token = Assert.Single(await PublishedAsync<EmailChangeRequested>(userId)).Token;

        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens
                .Where(item => item.UserId == userId && item.Purpose == UserTokenPurpose.EmailChange)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Cancellation);
        }

        Assert.Equal("EmailChange.TokenInvalid", await CodeAsync(await ConfirmAsync(client, token)));

        var disabledEmail = NewEmail();
        var disabledId = await RegisterAsync(client, disabledEmail);
        var disabledAccess = await SignInAsync(client, disabledEmail);
        await RequestAsync(client, disabledAccess, Password, NewEmail());
        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users
                .Where(user => user.Id == disabledId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsDisabled, true), Cancellation);
        }

        Assert.Equal("EmailChange.TokenInvalid", await CodeAsync(await ConfirmAsync(client, Assert.Single(await PublishedAsync<EmailChangeRequested>(disabledId)).Token)));
        Assert.Equal(disabledEmail, (await LoadUserAsync(disabledId)).Email.Value);
    }

    [Fact]
    public async Task ARequest_NeedsASignIn()
    {
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/me/email", new ChangeEmailRequest(Password, NewEmail()), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string accessToken, string? password, string? newEmail)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/me/email") { Content = JsonContent.Create(new ChangeEmailRequest(password, newEmail)) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request, Cancellation);
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string? token)
        => client.PostAsJsonAsync("/email-change/confirm", new ConfirmEmailChangeRequest(token), Cancellation);

    private async Task<List<T>> PublishedAsync<T>(Guid userId)
    {
        await using var scope = api.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().OutboxMessages
            .Where(message => message.Type == IntegrationEvents.MessageTypeOf(typeof(T)))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        return [.. payloads.Where(payload => payload.Contains(userId.ToString(), StringComparison.Ordinal))
            .Select(payload => JsonSerializer.Deserialize<T>(payload, IntegrationEvents.SerializerOptions)!)];
    }

    private async Task<List<UserToken>> TokensAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens.AsNoTracking()
            .Where(token => token.UserId == userId)
            .ToListAsync(Cancellation);
    }

    private async Task MoveTokensBackAsync(Guid userId, TimeSpan by)
    {
        await using var scope = api.CreateScope();
        var created = DateTimeOffset.UtcNow - by;
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens
            .Where(token => token.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.CreatedAt, created), Cancellation);
    }

    private async Task<Auth.Domain.Users.User> LoadUserAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId, Cancellation);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString();
}
