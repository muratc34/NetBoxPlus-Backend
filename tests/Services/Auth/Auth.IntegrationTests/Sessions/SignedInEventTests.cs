using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.API.Sessions;
using Auth.Application.Sessions;
using Auth.Contracts;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Sessions;

/// <summary>Which sessions are announced as sign-ins: registering and signing in, not renewing or changing the password.</summary>
[Collection(nameof(AuthApiCollection))]
public sealed class SignedInEventTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";
    private const string Firefox = "Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegisteringAndSigningIn_AreAnnounced_RenewingAndChangingThePasswordAreNot()
    {
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Firefox);
        var email = $"signed-in-{Guid.NewGuid():N}@example.com";

        var registered = await client.PostAsJsonAsync("/register", new RegisterRequest(email, Password), Cancellation);
        var account = (await registered.Content.ReadFromJsonAsync<RegisterResponse>(Cancellation))!;
        var signIn = await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, Password), Cancellation);
        var tokens = (await signIn.Content.ReadFromJsonAsync<TokenPair>(Cancellation))!;

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/refresh", new RefreshRequest(tokens.RefreshToken), Cancellation)).StatusCode);
        using var change = new HttpRequestMessage(HttpMethod.Post, "/me/password") { Content = JsonContent.Create(new ChangePasswordRequest(Password, Password + " again")) };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(change, Cancellation)).StatusCode);

        var announced = (await PublishedAsync(account.Id)).OrderBy(item => item.SignedInAt).ToList();
        Assert.Equal(2, announced.Count);
        Assert.Equal((true, "Customer", "Firefox · Linux"), (announced[0].Registration, announced[0].Kind, announced[0].DeviceLabel));
        Assert.Equal((false, tokens.SessionId), (announced[1].Registration, announced[1].SessionId));
    }

    private async Task<List<SignedIn>> PublishedAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().OutboxMessages
            .Where(message => message.Type == IntegrationEvents.MessageTypeOf(typeof(SignedIn)))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<SignedIn>(payload, IntegrationEvents.SerializerOptions)!).Where(item => item.UserId == userId)];
    }
}
