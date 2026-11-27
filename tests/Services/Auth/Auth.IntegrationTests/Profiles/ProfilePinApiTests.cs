using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.API.Profiles;
using Auth.API.Sessions;
using Auth.Application.Profiles.GetProfiles;
using Auth.Application.Sessions;
using Auth.Contracts;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Auth.IntegrationTests.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Profiles;

/// <summary>A PIN on a profile, and opening the profile for one sign-in.</summary>
[Collection(nameof(AuthApiCollection))]
public sealed class ProfilePinApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";
    private const string UnlockedClaim = "profile_unlocked";
    private static readonly JsonSerializerOptions Json = CreateJson();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APin_LocksTheProfile_AndTakingItOff_OpensIt_WithEachStepPublished()
    {
        var account = await RegisterAsync();
        var signIn = await SignInAsync(account.Email);
        var profile = Assert.Single(await ProfilesAsync(signIn.Client)).Id;

        Assert.Equal(HttpStatusCode.NoContent, (await SetPinAsync(signIn.Client, profile, Password, "1234")).StatusCode);
        Assert.True(Assert.Single(await ProfilesAsync(signIn.Client)).Locked);

        Assert.Equal(HttpStatusCode.NoContent, (await SetPinAsync(signIn.Client, profile, Password, null)).StatusCode);
        Assert.False(Assert.Single(await ProfilesAsync(signIn.Client)).Locked);

        // Taking off a PIN that is not there changes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await SetPinAsync(signIn.Client, profile, Password, null)).StatusCode);

        Assert.Equal(
            [(2, true), (3, false)],
            (await PublishedAsync<ProfileChanged>(account.UserId)).OrderBy(changed => changed.Revision).Select(changed => (changed.Revision, changed.Locked)));
    }

    [Fact]
    public async Task ThePin_AsksForThePasswordAgain_AndFourDigits()
    {
        var account = await RegisterAsync();
        var signIn = await SignInAsync(account.Email);
        var profile = Assert.Single(await ProfilesAsync(signIn.Client)).Id;

        await AssertProblemAsync(await SetPinAsync(signIn.Client, profile, "not the password", "1234"), HttpStatusCode.BadRequest, "User.PasswordIncorrect");
        await AssertProblemAsync(await SetPinAsync(signIn.Client, profile, Password, "12a4"), HttpStatusCode.BadRequest, "Profile.PinInvalid");
        await AssertProblemAsync(await SetPinAsync(signIn.Client, profile, Password, "12345"), HttpStatusCode.BadRequest, "Profile.PinInvalid");
        await AssertProblemAsync(await SetPinAsync(signIn.Client, Guid.NewGuid(), Password, "1234"), HttpStatusCode.NotFound, "Profile.NotFound");

        Assert.False(Assert.Single(await ProfilesAsync(signIn.Client)).Locked);
        await using var scope = api.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().SingleAsync(item => item.Id == account.UserId, Cancellation);
        Assert.Equal(1, user.FailedSignInCount);
    }

    [Fact]
    public async Task Unlocking_NamesTheProfile_InTheNextTokens_OfThatSignInOnly()
    {
        var account = await RegisterAsync();
        var phone = await SignInAsync(account.Email);
        var tv = await SignInAsync(account.Email);
        var profile = Assert.Single(await ProfilesAsync(phone.Client)).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await SetPinAsync(phone.Client, profile, Password, "2468")).StatusCode);

        await AssertProblemAsync(await UnlockAsync(phone.Client, profile, "1357"), HttpStatusCode.BadRequest, "Profile.PinIncorrect");
        Assert.Equal(HttpStatusCode.NoContent, (await UnlockAsync(phone.Client, profile, "2468")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UnlockAsync(phone.Client, profile, "2468")).StatusCode);

        var phoneNext = await RefreshAsync(phone.Tokens);
        Assert.Equal([profile.ToString()], UnlockedIn(phoneNext));
        Assert.Equal([profile.ToString()], UnlockedIn(await RefreshAsync(phoneNext)));
        Assert.Empty(UnlockedIn(await RefreshAsync(tv.Tokens)));

        // A right PIN clears the wrong one before it.
        await using var scope = api.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<Profile>().AsNoTracking().SingleAsync(item => item.Id == profile, Cancellation);
        Assert.Equal(0, stored.FailedPinCount);
    }

    [Fact]
    public async Task ChangingThePin_ClosesEverySignIn_ThatHadOpenedTheProfile()
    {
        var account = await RegisterAsync();
        var signIn = await SignInAsync(account.Email);
        var profile = Assert.Single(await ProfilesAsync(signIn.Client)).Id;
        await SetPinAsync(signIn.Client, profile, Password, "1111");
        await UnlockAsync(signIn.Client, profile, "1111");

        await SetPinAsync(signIn.Client, profile, Password, "2222");

        Assert.Empty(UnlockedIn(await RefreshAsync(signIn.Tokens)));
    }

    [Fact]
    public async Task AProfileWithoutAPin_IsOpenAlready_AndAnotherAccountsProfileIsNotFound()
    {
        var account = await RegisterAsync();
        var signIn = await SignInAsync(account.Email);
        var other = await SignInAsync((await RegisterAsync()).Email);
        var profile = Assert.Single(await ProfilesAsync(signIn.Client)).Id;

        Assert.Equal(HttpStatusCode.NoContent, (await UnlockAsync(signIn.Client, profile, "0000")).StatusCode);
        Assert.Empty(UnlockedIn(await RefreshAsync(signIn.Tokens)));

        await SetPinAsync(signIn.Client, profile, Password, "0000");
        await AssertProblemAsync(await UnlockAsync(other.Client, profile, "0000"), HttpStatusCode.NotFound, "Profile.NotFound");
        await AssertProblemAsync(await UnlockAsync(signIn.Client, profile, "00"), HttpStatusCode.BadRequest, "Profile.PinInvalid");
    }

    [Fact]
    public async Task FiveWrongPins_LockTheProfile_EvenAgainstTheRightOne_UntilTheLockEnds()
    {
        var account = await RegisterAsync();
        var signIn = await SignInAsync(account.Email);
        var profile = Assert.Single(await ProfilesAsync(signIn.Client)).Id;
        await SetPinAsync(signIn.Client, profile, Password, "4321");

        // Tried at once: every attempt is counted, none overwrites another.
        var wrong = await Task.WhenAll(Enumerable.Range(0, Profile.MaxFailedPins).Select(_ => UnlockAsync(signIn.Client, profile, "9999")));
        Assert.All(wrong, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));

        await AssertProblemAsync(await UnlockAsync(signIn.Client, profile, "4321"), HttpStatusCode.Conflict, "Profile.PinLocked");

        await using (var scope = api.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Set<Profile>()
                .Where(item => item.Id == profile)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PinLockedUntil, DateTimeOffset.UtcNow.AddSeconds(-1)), Cancellation);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await UnlockAsync(signIn.Client, profile, "4321")).StatusCode);
    }

    private async Task<(string Email, Guid UserId)> RegisterAsync()
    {
        using var client = api.CreateClient();
        var email = $"pin-{Guid.NewGuid():N}@example.com";
        var registered = await client.PostAsJsonAsync("/register", new RegisterRequest(email, Password), Cancellation);
        Assert.True(registered.IsSuccessStatusCode, await registered.Content.ReadAsStringAsync(Cancellation));
        return (email, (await registered.Content.ReadFromJsonAsync<RegisterResponse>(Cancellation))!.Id);
    }

    private async Task<(HttpClient Client, TokenPair Tokens)> SignInAsync(string email)
    {
        var client = api.CreateClient();
        var tokens = await (await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, Password), Cancellation)).Content.ReadFromJsonAsync<TokenPair>(Cancellation);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return (client, tokens);
    }

    private async Task<TokenPair> RefreshAsync(TokenPair tokens)
    {
        using var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/refresh", new RefreshRequest(tokens.RefreshToken), Cancellation);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancellation));
        return (await response.Content.ReadFromJsonAsync<TokenPair>(Cancellation))!;
    }

    private static List<string> UnlockedIn(TokenPair tokens)
        => [.. new JsonWebToken(tokens.AccessToken).Claims.Where(claim => claim.Type == UnlockedClaim).Select(claim => claim.Value)];

    private static Task<HttpResponseMessage> SetPinAsync(HttpClient client, Guid profileId, string password, string? pin)
        => client.PutAsJsonAsync($"/profiles/{profileId}/pin", new ProfilePinRequest(password, pin), Json, Cancellation);

    private static Task<HttpResponseMessage> UnlockAsync(HttpClient client, Guid profileId, string pin)
        => client.PostAsJsonAsync($"/profiles/{profileId}/unlock", new UnlockProfileRequest(pin), Json, Cancellation);

    private static async Task<List<ProfileResponse>> ProfilesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/profiles", Cancellation);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Cancellation));
        return (await response.Content.ReadFromJsonAsync<List<ProfileResponse>>(Json, Cancellation))!;
    }

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

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.True(response.StatusCode == status, $"{(int)response.StatusCode}: {body}");
        // A rule broken before the handler runs is listed by field (errorCodes), the others as the problem's code.
        Assert.Contains($"\"{code}\"", body, StringComparison.Ordinal);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
