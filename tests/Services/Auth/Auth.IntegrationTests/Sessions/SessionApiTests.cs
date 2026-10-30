using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.API.Sessions;
using Auth.Application.Sessions;
using Auth.Application.Users.GetMe;
using Auth.Contracts;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Sessions;

[Collection(nameof(AuthApiCollection))]
public sealed class SessionApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_ThenSignIn_ReturnsATokenPairForTheUser()
    {
        using var client = api.CreateClient();
        var email = NewEmail();

        var id = await RegisterAsync(client, email);
        var response = await client.PostAsJsonAsync("/sign-in", new SignInRequest(email.ToUpperInvariant(), Password), Cancellation);
        var pair = await response.Content.ReadFromJsonAsync<TokenPair>(Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var token = new JsonWebToken(pair!.AccessToken);
        Assert.Equal(id.ToString(), token.Subject);
        Assert.Equal(email, token.GetClaim("email").Value);
        Assert.Equal(43, pair.RefreshToken.Length);
        Assert.True(pair.RefreshTokenExpiresAt > pair.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task Register_WithATakenAddress_Returns409()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        var response = await client.PostAsJsonAsync("/register", new RegisterRequest($"  {email.ToUpperInvariant()} ", Password), Cancellation);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("User.EmailTaken", await CodeAsync(response));
    }

    [Fact]
    public async Task Register_WithInvalidInput_ReportsEachField()
    {
        using var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/register", new RegisterRequest("not-an-address", "short"), Cancellation);
        var problem = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("User.EmailInvalid", problem.GetProperty("errorCodes").GetProperty("email")[0].GetString());
        Assert.Equal("User.PasswordTooShort", problem.GetProperty("errorCodes").GetProperty("password")[0].GetString());
    }

    [Fact]
    public async Task Register_PublishesUserRegisteredThroughTheOutbox()
    {
        using var client = api.CreateClient();
        var email = NewEmail();

        var id = await RegisterAsync(client, email);

        await using var scope = api.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().OutboxMessages
            .Where(message => message.Type == IntegrationEvents.MessageTypeOf(typeof(UserRegistered)))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        var registered = payloads
            .Select(payload => JsonSerializer.Deserialize<UserRegistered>(payload, IntegrationEvents.SerializerOptions)!)
            .Single(message => message.UserId == id);

        Assert.Equal(email, registered.Email);
    }

    [Fact]
    public async Task WrongPassword_UnknownAddress_AndLockedAccount_AllLookTheSame()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);

        var wrongPassword = await SignInRawAsync(client, email, "wrong password!!");
        var unknown = await SignInRawAsync(client, NewEmail(), Password);

        for (var attempt = 1; attempt < User.MaxFailedSignIns; attempt++)
        {
            await SignInRawAsync(client, email, "wrong password!!");
        }

        var locked = await SignInRawAsync(client, email, Password);

        foreach (var response in new[] { wrongPassword, unknown, locked })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("User.InvalidCredentials", await CodeAsync(response));
        }

        var user = await LoadUserAsync(email);
        Assert.NotNull(user.LockedOutUntil);
    }

    [Fact]
    public async Task SuccessfulSignIn_ResetsTheFailedCount()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        await SignInRawAsync(client, email, "wrong password!!");
        await SignInRawAsync(client, email, "wrong password!!");

        await SignInAsync(client, email);

        Assert.Equal(0, (await LoadUserAsync(email)).FailedSignInCount);
    }

    [Fact]
    public async Task Refresh_RotatesTheToken()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var first = await SignInAsync(client, email);

        var response = await client.PostAsJsonAsync("/refresh", new RefreshRequest(first.RefreshToken), Cancellation);
        var second = await response.Content.ReadFromJsonAsync<TokenPair>(Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(first.RefreshToken, second!.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await RefreshRawAsync(client, second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ReusedRefreshToken_RevokesTheWholeFamily()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var stolen = await SignInAsync(client, email);
        var rotated = await (await RefreshRawAsync(client, stolen.RefreshToken)).Content.ReadFromJsonAsync<TokenPair>(Cancellation);

        var reuse = await RefreshRawAsync(client, stolen.RefreshToken);
        var afterReuse = await RefreshRawAsync(client, rotated!.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal("RefreshToken.Invalid", await CodeAsync(reuse));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task OtherSessionsSurviveAReuseInOneFamily()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var phone = await SignInAsync(client, email);
        var laptop = await SignInAsync(client, email);
        await RefreshRawAsync(client, phone.RefreshToken);

        await RefreshRawAsync(client, phone.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, (await RefreshRawAsync(client, laptop.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task UnknownRefreshToken_Returns401()
    {
        using var client = api.CreateClient();

        var response = await RefreshRawAsync(client, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignOut_EndsTheSession_AndAlwaysReturns204()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var pair = await SignInAsync(client, email);

        var signOut = await client.PostAsJsonAsync("/sign-out", new RefreshRequest(pair.RefreshToken), Cancellation);
        var unknown = await client.PostAsJsonAsync("/sign-out", new RefreshRequest("unknown"), Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshRawAsync(client, pair.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsTheAccountWithItsRolesAndPermissions()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var id = await RegisterAsync(client, email);
        await GrantAsync(email, Permissions.Auth.UsersRead, Permissions.Sample.NotesDelete);
        var pair = await SignInAsync(client, email);

        var me = await GetMeAsync(client, pair.AccessToken);

        Assert.Equal(id, me.Id);
        Assert.Equal(email, me.Email);
        Assert.Single(me.Roles);
        Assert.Equal([Permissions.Auth.UsersRead, Permissions.Sample.NotesDelete], me.Permissions);
        Assert.Equal(me.Permissions, new JsonWebToken(pair.AccessToken).Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value));
    }

    [Fact]
    public async Task Me_WithoutAToken_Returns401()
    {
        using var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/me", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task NewPermissions_AppearInTheNextRefreshedToken()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var before = await SignInAsync(client, email);

        await GrantAsync(email, Permissions.Sample.NotesDelete);
        var after = await (await RefreshRawAsync(client, before.RefreshToken)).Content.ReadFromJsonAsync<TokenPair>(Cancellation);

        Assert.DoesNotContain(new JsonWebToken(before.AccessToken).Claims, claim => claim.Type == "permission");
        Assert.Contains(new JsonWebToken(after!.AccessToken).Claims, claim => claim is { Type: "permission", Value: Permissions.Sample.NotesDelete });
    }

    [Fact]
    public async Task ChangePassword_EndsEverySession_AndReturnsANewOne()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var otherDevice = await SignInAsync(client, email);
        var current = await SignInAsync(client, email);
        const string newPassword = "a brand new passphrase";

        var wrong = await ChangePasswordAsync(client, current.AccessToken, "not my password!", newPassword);
        var changed = await ChangePasswordAsync(client, current.AccessToken, Password, newPassword);
        var pair = await changed.Content.ReadFromJsonAsync<TokenPair>(Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("User.PasswordIncorrect", await CodeAsync(wrong));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshRawAsync(client, otherDevice.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshRawAsync(client, current.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshRawAsync(client, pair!.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInRawAsync(client, email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInRawAsync(client, email, newPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithAWrongCurrentPassword_CountsAsAFailedSignIn()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var pair = await SignInAsync(client, email);

        await ChangePasswordAsync(client, pair.AccessToken, "not my password!", "a brand new passphrase");

        Assert.Equal(1, (await LoadUserAsync(email)).FailedSignInCount);
    }

    [Fact]
    public async Task AdminSignIn_RequiresTheAdminPermission()
    {
        using var client = api.CreateClient();
        var customer = NewEmail();
        var admin = NewEmail();
        await RegisterAsync(client, customer);
        await RegisterAsync(client, admin);
        await GrantAsync(admin, Permissions.Auth.AdminSignIn);

        var denied = await client.PostAsJsonAsync("/admin/sign-in", new SignInRequest(customer, Password), Cancellation);
        var allowed = await client.PostAsJsonAsync("/admin/sign-in", new SignInRequest(admin, Password), Cancellation);
        var pair = await allowed.Content.ReadFromJsonAsync<TokenPair>(Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("User.InvalidCredentials", await CodeAsync(denied));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        // Admin sessions last one working day, not extended.
        Assert.InRange(pair!.RefreshTokenExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromHours(7.9), TimeSpan.FromHours(8));
    }

    [Fact]
    public async Task AdminSession_EndsWhenTheAdminPermissionIsTakenAway()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var roleId = await GrantAsync(email, Permissions.Auth.AdminSignIn);
        var pair = await (await client.PostAsJsonAsync("/admin/sign-in", new SignInRequest(email, Password), Cancellation))
            .Content.ReadFromJsonAsync<TokenPair>(Cancellation);

        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            (await context.Roles.SingleAsync(role => role.Id == roleId, Cancellation)).SetPermissions([]);
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshRawAsync(client, pair!.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task DisabledAccount_CannotRefresh()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email);
        var pair = await SignInAsync(client, email);

        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            (await context.Users.SingleAsync(user => user.Email == EmailAddress.Create(email).Value, Cancellation)).Disable();
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshRawAsync(client, pair.RefreshToken)).StatusCode);
        Assert.Equal("User.InvalidCredentials", await CodeAsync(await SignInRawAsync(client, email, Password)));
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

    private static async Task<TokenPair> SignInAsync(HttpClient client, string email)
    {
        var response = await SignInRawAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPair>(Cancellation))!;
    }

    private static Task<HttpResponseMessage> RefreshRawAsync(HttpClient client, string refreshToken)
        => client.PostAsJsonAsync("/refresh", new RefreshRequest(refreshToken), Cancellation);

    private static async Task<MeResponse> GetMeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MeResponse>(Cancellation))!;
    }

    private static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string accessToken, string current, string next)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/me/password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(current, next)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request, Cancellation);
    }

    /// <summary>Gives the user a new role holding <paramref name="permissions"/>; returns the role id.</summary>
    private async Task<Guid> GrantAsync(string email, params string[] permissions)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var role = Role.Create($"Role {Guid.NewGuid():N}", permissions).Value;
        var user = await context.Users.Include(user => user.Roles)
            .SingleAsync(user => user.Email == EmailAddress.Create(email).Value, Cancellation);
        context.Roles.Add(role);
        user.AssignRole(role.Id);
        await context.SaveChangesAsync(Cancellation);
        return role.Id;
    }

    private async Task<User> LoadUserAsync(string email)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking()
            .SingleAsync(user => user.Email == EmailAddress.Create(email).Value, Cancellation);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await JsonAsync(response)).GetProperty("code").GetString();
}
