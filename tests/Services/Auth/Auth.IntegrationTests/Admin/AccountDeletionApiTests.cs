using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.API.Sessions;
using Auth.Application.Sessions;
using Auth.Contracts;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Auth.IntegrationTests.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;

namespace Auth.IntegrationTests.Admin;

/// <summary>An account deleted by its user or by an administrator.</summary>
[Collection(nameof(AuthApiCollection))]
public sealed class AccountDeletionApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheUser_DeletesTheAccount_WhichIsAnonymized_Announced_AndGoneEverywhere()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var pair = await SignInAsync(client, email);
        await client.PostAsJsonAsync("/password-reset", new RequestPasswordResetRequest(email), Cancellation);

        var response = await SendAsync(client, HttpMethod.Post, "/me/deletion", pair.AccessToken, new DeleteAccountRequest(Password));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stored = await LoadUserIgnoringDeletionAsync(userId);
        Assert.Equal(
            ($"deleted-{userId:N}@deleted.invalid", User.DeletedPasswordHash, true, 0, 0, 2),
            (stored.Email.Value, stored.PasswordHash, stored.IsDisabled, stored.Roles.Count, stored.Profiles.Count, stored.EmailRevision));
        Assert.NotNull(stored.DeletedAt);
        var deleted = Assert.Single(await PublishedAsync<UserDeleted>(userId));
        Assert.Equal(stored.DeletedAt!.Value, deleted.DeletedAt, TimeSpan.FromMilliseconds(1));
        Assert.DoesNotContain(email, await PayloadAsync<UserDeleted>(userId), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, Password), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/refresh", new RefreshRequest(pair.RefreshToken), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/me", pair.AccessToken)).StatusCode);
        var resetToken = Assert.Single(await PublishedAsync<PasswordResetRequested>(userId)).Token;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/password-reset/confirm", new ResetPasswordRequest(resetToken, "another correct horse battery"), Cancellation)).StatusCode);

        // Revoked in the database too, not only unreachable through the hidden account.
        await using (var scope = api.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            Assert.All(await context.RefreshTokens.AsNoTracking().Where(token => token.UserId == userId).ToListAsync(Cancellation), token => Assert.NotNull(token.RevokedAt));
            Assert.All(await context.UserTokens.AsNoTracking().Where(token => token.UserId == userId).ToListAsync(Cancellation), token => Assert.True(token.RevokedAt is not null || token.UsedAt is not null));
        }

        // The address is free again: a new account, with a new id.
        Assert.NotEqual(userId, await RegisterAsync(client, email));
    }

    [Fact]
    public async Task AWrongPassword_DeletesNothing_AndCountsAsAFailedSignIn()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        var userId = await RegisterAsync(client, email);
        var token = (await SignInAsync(client, email)).AccessToken;

        var wrong = await SendAsync(client, HttpMethod.Post, "/me/deletion", token, new DeleteAccountRequest("not the password at all"));
        var missing = await SendAsync(client, HttpMethod.Post, "/me/deletion", token, new DeleteAccountRequest(null));

        Assert.Equal((HttpStatusCode.BadRequest, "User.PasswordIncorrect"), (wrong.StatusCode, await CodeAsync(wrong)));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var stored = await LoadUserIgnoringDeletionAsync(userId);
        Assert.Equal((email, (DateTimeOffset?)null, 1), (stored.Email.Value, stored.DeletedAt, stored.FailedSignInCount));
        Assert.Empty(await PublishedAsync<UserDeleted>(userId));
    }

    [Fact]
    public async Task AnAdministrator_DeletesAnAccount_AndItIsAudited()
    {
        using var client = api.CreateClient();
        var (_, adminToken) = await AdminAsync(client, Permissions.Auth.UsersRead, Permissions.Auth.UsersDelete);
        var targetEmail = NewEmail();
        var targetId = await RegisterAsync(client, targetEmail);

        var response = await SendAsync(client, HttpMethod.Delete, $"/admin/users/{targetId}", adminToken);
        var again = await SendAsync(client, HttpMethod.Delete, $"/admin/users/{targetId}", adminToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Single(await PublishedAsync<UserDeleted>(targetId));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/admin/users/{targetId}", adminToken)).StatusCode);
        Assert.Contains(await AuditActionsAsync(targetId), action => action.Contains("DeleteUser", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deleting_NeedsItsOwnPermission_AndNeverTheCallersOwnAccount()
    {
        using var client = api.CreateClient();
        var (_, writer) = await AdminAsync(client, Permissions.Auth.UsersRead, Permissions.Auth.UsersWrite);
        var (adminId, deleter) = await AdminAsync(client, Permissions.Auth.UsersDelete);
        var targetId = await RegisterAsync(client, NewEmail());

        var forbidden = await SendAsync(client, HttpMethod.Delete, $"/admin/users/{targetId}", writer);
        var self = await SendAsync(client, HttpMethod.Delete, $"/admin/users/{adminId}", deleter);
        var unknown = await SendAsync(client, HttpMethod.Delete, $"/admin/users/{Guid.NewGuid()}", deleter);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "User.CannotDeleteSelf"), (self.StatusCode, await CodeAsync(self)));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty(await PublishedAsync<UserDeleted>(targetId));
    }

    [Fact]
    public async Task AnAccountWithARoleTheCallerDoesNotHold_IsNotDeleted()
    {
        using var client = api.CreateClient();
        var (_, deleter) = await AdminAsync(client, Permissions.Auth.UsersDelete);
        var targetEmail = NewEmail();
        var targetId = await RegisterAsync(client, targetEmail);
        await GrantAsync(targetEmail, Permissions.Auth.RolesWrite);

        var response = await SendAsync(client, HttpMethod.Delete, $"/admin/users/{targetId}", deleter);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null((await LoadUserIgnoringDeletionAsync(targetId)).DeletedAt);
    }

    [Fact]
    public async Task TheLastAdministrator_IsNotDeleted()
    {
        using var client = api.CreateClient();
        var (_, deleter) = await AdminAsync(client, [.. Permissions.All]);
        var lastEmail = NewEmail();
        var lastId = await RegisterAsync(client, lastEmail);
        var systemRole = await MakeTheOnlyAdministratorAsync(lastId);

        var refused = await SendAsync(client, HttpMethod.Delete, $"/admin/users/{lastId}", deleter);

        Assert.Equal((HttpStatusCode.Conflict, "User.LastAdministrator"), (refused.StatusCode, await CodeAsync(refused)));

        var otherId = await RegisterAsync(client, NewEmail());
        await AssignDirectlyAsync(otherId, systemRole);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/admin/users/{lastId}", deleter)).StatusCode);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<(Guid Id, string Token)> AdminAsync(HttpClient client, params string[] permissions)
    {
        var email = NewEmail();
        var id = await RegisterAsync(client, email);
        await GrantAsync(email, permissions);
        return (id, (await SignInAsync(client, email)).AccessToken);
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/register", new RegisterRequest(email, Password), Cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegisterResponse>(Cancellation))!.Id;
    }

    private static async Task<TokenPair> SignInAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/sign-in", new SignInRequest(email, Password), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPair>(Cancellation))!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, Cancellation);
    }

    private async Task GrantAsync(string email, params string[] permissions)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var role = Role.Create($"Role {Guid.NewGuid():N}", permissions).Value;
        var user = await context.Users.Include(user => user.Roles)
            .SingleAsync(user => user.Email == EmailAddress.Create(email).Value, Cancellation);
        context.Roles.Add(role);
        user.AssignRole(role.Id);
        await context.SaveChangesAsync(Cancellation);
    }

    /// <summary>Takes the system role away from every account, then gives it to <paramref name="userId"/> only.</summary>
    private async Task<Guid> MakeTheOnlyAdministratorAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var role = await context.Roles.FirstOrDefaultAsync(item => item.IsSystem, Cancellation);
        if (role is null)
        {
            role = Role.CreateAdministrator();
            context.Roles.Add(role);
            await context.SaveChangesAsync(Cancellation);
        }

        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"UserRoles\" WHERE \"RoleId\" = {role.Id}", Cancellation);
        await AssignDirectlyAsync(userId, role.Id);
        return role.Id;
    }

    private async Task AssignDirectlyAsync(Guid userId, Guid roleId)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = await context.Users.Include(user => user.Roles).SingleAsync(user => user.Id == userId, Cancellation);
        user.AssignRole(roleId);
        await context.SaveChangesAsync(Cancellation);
    }

    private async Task<User> LoadUserIgnoringDeletionAsync(Guid userId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.IgnoreQueryFilters().AsNoTracking()
            .Include(user => user.Roles)
            .SingleAsync(user => user.Id == userId, Cancellation);
    }

    private async Task<List<string>> AuditActionsAsync(Guid targetId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().AuditLogEntries.AsNoTracking()
            .Where(entry => entry.TargetId == targetId)
            .Select(entry => entry.Action)
            .ToListAsync(Cancellation);
    }

    private async Task<string> PayloadAsync<T>(Guid userId)
    {
        await using var scope = api.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().OutboxMessages
            .Where(message => message.Type == IntegrationEvents.MessageTypeOf(typeof(T)))
            .Select(message => message.Payload)
            .ToListAsync(Cancellation);
        return payloads.Single(payload => payload.Contains(userId.ToString(), StringComparison.Ordinal));
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

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString();
}
