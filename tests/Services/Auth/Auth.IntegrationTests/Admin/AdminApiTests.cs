using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Auth.API.Admin;
using Auth.API.Sessions;
using Auth.Application.Roles.GetRole;
using Auth.Application.Roles.GetRoles;
using Auth.Application.Sessions;
using Auth.Application.Users.GetUser;
using Auth.Application.Users.GetUsers;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Auth.IntegrationTests.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Application.Paging;
using Shared.Infrastructure.Persistence.Auditing;

namespace Auth.IntegrationTests.Admin;

[Collection(nameof(AuthApiCollection))]
public sealed class AdminApiTests(AuthApiFixture api)
{
    private const string Password = "correct horse battery staple";

    private static readonly string[] UserManager = [Permissions.Auth.UsersRead, Permissions.Auth.UsersWrite];

    private static readonly string[] RoleManager = [Permissions.Auth.RolesRead, Permissions.Auth.RolesWrite];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AdminEndpoints_WithoutAToken_Return401_AndWithoutThePermission_403()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, Permissions.Auth.RolesRead);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/admin/users", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/admin/users", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, "/admin/roles", token, new RoleRequest("Editor", []))).StatusCode);
    }

    [Fact]
    public async Task GetUsers_FindsByPartOfTheAddress_IgnoringCase()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, UserManager);
        var marker = Guid.NewGuid().ToString("N")[..12];
        var first = await RegisterAsync(client, $"first-{marker}@example.com");
        var second = await RegisterAsync(client, $"second-{marker}@example.com");

        var page = await ReadAsync<Page<UserSummary>>(await SendAsync(client, HttpMethod.Get, $"/admin/users?search={marker.ToUpperInvariant()}&pageSize=1", token));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second, Assert.Single(page.Items).Id);

        var next = await ReadAsync<Page<UserSummary>>(await SendAsync(client, HttpMethod.Get, $"/admin/users?search={marker}&page=2&pageSize=1", token));
        Assert.Equal(first, Assert.Single(next.Items).Id);
    }

    [Fact]
    public async Task GetUsers_TreatsWildcardsLiterally()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, UserManager);

        var page = await ReadAsync<Page<UserSummary>>(await SendAsync(client, HttpMethod.Get, "/admin/users?search=%25_", token));

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task GetUsers_WithAnOversizedPage_Returns400()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, UserManager);

        var response = await SendAsync(client, HttpMethod.Get, $"/admin/users?pageSize={PageLimits.MaxPageSize + 1}", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PageLimits.OutOfRange.Code, (await JsonAsync(response)).GetProperty("errorCodes").GetProperty("page")[0].GetString());
    }

    [Fact]
    public async Task GetUser_ShowsRolesAndLockState()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, UserManager);
        var email = NewEmail();
        var id = await RegisterAsync(client, email);
        var roleId = await GrantAsync(email, Permissions.Auth.UsersRead);
        await SignInRawAsync(client, email, "wrong password!!");

        var user = await ReadAsync<UserDetails>(await SendAsync(client, HttpMethod.Get, $"/admin/users/{id}", token));

        Assert.Equal(email, user.Email);
        Assert.Equal(1, user.FailedSignInCount);
        Assert.Equal(roleId, Assert.Single(user.Roles).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/admin/users/{Guid.CreateVersion7()}", token)).StatusCode);
    }

    [Fact]
    public async Task DisableUser_EndsTheSessionsAndStopsSignIn_EnableLetsItBackIn()
    {
        using var client = api.CreateClient();
        var (adminId, token) = await AdminAsync(client, UserManager);
        var email = NewEmail();
        var id = await RegisterAsync(client, email);
        var pair = await SignInAsync(client, email);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"/admin/users/{id}/disable", token)).StatusCode);

        // Revoked at once, not only refused at the next refresh.
        await using (var scope = api.CreateScope())
        {
            Assert.All(
                await scope.ServiceProvider.GetRequiredService<AuthDbContext>().RefreshTokens.AsNoTracking()
                    .Where(refreshToken => refreshToken.UserId == id)
                    .ToListAsync(Cancellation),
                refreshToken => Assert.NotNull(refreshToken.RevokedAt));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/refresh", new RefreshRequest(pair.RefreshToken), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInRawAsync(client, email, Password)).StatusCode);

        var entry = await LastAuditEntryAsync(id);
        Assert.Equal("DisableUser", entry.Action);
        Assert.Equal(adminId, entry.ActorId);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, $"/admin/users/{id}/enable", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInRawAsync(client, email, Password)).StatusCode);
        Assert.Equal("EnableUser", (await LastAuditEntryAsync(id)).Action);
    }

    [Fact]
    public async Task Administrator_CannotDisableThemself_OrRemoveTheirOwnRole()
    {
        using var client = api.CreateClient();
        var (adminId, token) = await AdminAsync(client, UserManager);
        var roleId = (await ReadAsync<UserDetails>(await SendAsync(client, HttpMethod.Get, $"/admin/users/{adminId}", token))).Roles.Single().Id;

        var disable = await SendAsync(client, HttpMethod.Post, $"/admin/users/{adminId}/disable", token);
        var remove = await SendAsync(client, HttpMethod.Delete, $"/admin/users/{adminId}/roles/{roleId}", token);

        foreach (var response in new[] { disable, remove })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("User.CannotModifySelf", await CodeAsync(response));
        }
    }

    [Fact]
    public async Task AssignRole_GivesThePermissionsWithTheNextToken_AndIsAudited()
    {
        using var client = api.CreateClient();
        var (adminId, token) = await AdminAsync(client, [.. UserManager, Permissions.Auth.RolesRead]);
        var email = NewEmail();
        var id = await RegisterAsync(client, email);
        var pair = await SignInAsync(client, email);
        var roleId = await CreateRoleDirectlyAsync(Permissions.Auth.RolesRead);

        var response = await SendAsync(client, HttpMethod.Put, $"/admin/users/{id}/roles/{roleId}", token);
        var refreshed = await ReadAsync<TokenPair>(await client.PostAsJsonAsync("/refresh", new RefreshRequest(pair.RefreshToken), Cancellation));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(
            new JsonWebToken(refreshed.AccessToken).Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value),
            permission => permission == Permissions.Auth.RolesRead);

        var entry = await LastAuditEntryAsync(id);
        Assert.Equal("AssignRole", entry.Action);
        Assert.Equal(adminId, entry.ActorId);
        Assert.Equal(roleId, JsonDocument.Parse(entry.Details).RootElement.GetProperty("roleId").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/admin/users/{id}/roles/{roleId}", token)).StatusCode);
        Assert.Equal("RemoveRole", (await LastAuditEntryAsync(id)).Action);
    }

    [Fact]
    public async Task AssignRole_ThatHoldsMoreThanTheCaller_Returns403_AndRecordsNothing()
    {
        using var client = api.CreateClient();
        var (managerId, managerToken) = await AdminAsync(client, UserManager);
        var (_, otherToken) = await AdminAsync(client, UserManager);
        var everything = await CreateRoleDirectlyAsync([.. Permissions.All]);

        // The escalation this rule stops: a user manager giving themself, or being given, a bigger role.
        var self = await SendAsync(client, HttpMethod.Put, $"/admin/users/{managerId}/roles/{everything}", managerToken);
        var other = await SendAsync(client, HttpMethod.Put, $"/admin/users/{managerId}/roles/{everything}", otherToken);

        foreach (var forbidden in new[] { self, other })
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal("Role.PermissionNotHeld", await CodeAsync(forbidden));
        }

        Assert.Null(await LastAuditEntryOrNullAsync(managerId));
    }

    [Fact]
    public async Task Roles_CanBeCreatedChangedAndDeleted_EachStepAudited()
    {
        using var client = api.CreateClient();
        var (adminId, token) = await AdminAsync(client, [.. RoleManager, Permissions.Auth.UsersRead]);
        var name = $"Support {Guid.NewGuid():N}";

        var created = await SendAsync(client, HttpMethod.Post, "/admin/roles", token, new RoleRequest(name, [Permissions.Auth.UsersRead]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadAsync<CreateRoleResponse>(created)).Id;
        Assert.Equal($"/admin/roles/{id}", created.Headers.Location?.OriginalString);

        var duplicate = await SendAsync(client, HttpMethod.Post, "/admin/roles", token, new RoleRequest(name.ToUpperInvariant(), []));
        Assert.Equal("Role.NameTaken", await CodeAsync(duplicate));

        var updated = await SendAsync(client, HttpMethod.Put, $"/admin/roles/{id}", token, new RoleRequest($"{name} 2", [Permissions.Auth.RolesRead]));
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var role = await ReadAsync<RoleDetails>(await SendAsync(client, HttpMethod.Get, $"/admin/roles/{id}", token));
        Assert.Equal($"{name} 2", role.Name);
        Assert.Equal([Permissions.Auth.RolesRead], role.Permissions);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/admin/roles/{id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/admin/roles/{id}", token)).StatusCode);

        var actions = await AuditActionsAsync(id);
        Assert.Equal(["CreateRole", "UpdateRole", "DeleteRole"], actions.Select(entry => entry.Action));
        Assert.All(actions, entry => Assert.Equal(adminId, entry.ActorId));
    }

    [Fact]
    public async Task CreateRole_WithUnknownOrUnheldPermissions_IsRejected()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, RoleManager);

        var unknown = await SendAsync(client, HttpMethod.Post, "/admin/roles", token, new RoleRequest("Pilot", ["auth:planes:fly"]));
        var unheld = await SendAsync(client, HttpMethod.Post, "/admin/roles", token, new RoleRequest("Pilot", [Permissions.Auth.UsersWrite]));

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unheld.StatusCode);
        Assert.Equal("Role.PermissionNotHeld", await CodeAsync(unheld));
    }

    [Fact]
    public async Task UpdateRole_ThatWouldDropAPermissionTheCallerLacks_Returns403()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, RoleManager);
        var roleId = await CreateRoleDirectlyAsync(Permissions.Auth.UsersWrite);

        var response = await SendAsync(client, HttpMethod.Put, $"/admin/roles/{roleId}", token, new RoleRequest("Demoted", []));
        var delete = await SendAsync(client, HttpMethod.Delete, $"/admin/roles/{roleId}", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task SystemRole_CannotBeChangedOrDeleted()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, [.. Permissions.All]);
        var systemRoleId = await EnsureSystemRoleAsync();

        var update = await SendAsync(client, HttpMethod.Put, $"/admin/roles/{systemRoleId}", token, new RoleRequest("Owner", []));
        var delete = await SendAsync(client, HttpMethod.Delete, $"/admin/roles/{systemRoleId}", token);

        foreach (var response in new[] { update, delete })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Role.SystemRole", await CodeAsync(response));
        }
    }

    [Fact]
    public async Task GetRoles_CountsHolders_AndPermissionsListTheCatalog()
    {
        using var client = api.CreateClient();
        var (_, token) = await AdminAsync(client, Permissions.Auth.RolesRead);
        var email = NewEmail();
        await RegisterAsync(client, email);
        var roleId = await GrantAsync(email, Permissions.Auth.UsersRead);

        var roles = await ReadAsync<List<RoleSummary>>(await SendAsync(client, HttpMethod.Get, "/admin/roles", token));
        var permissions = await ReadAsync<List<string>>(await SendAsync(client, HttpMethod.Get, "/admin/permissions", token));

        Assert.Equal(1, roles.Single(role => role.Id == roleId).UserCount);
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), permissions);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>A signed-in account holding exactly <paramref name="permissions"/> through one role of its own.</summary>
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

    private static Task<HttpResponseMessage> SignInRawAsync(HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/sign-in", new SignInRequest(email, password), Cancellation);

    private static async Task<TokenPair> SignInAsync(HttpClient client, string email)
        => await ReadAsync<TokenPair>(await SignInRawAsync(client, email, Password));

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

    private async Task<Guid> CreateRoleDirectlyAsync(params string[] permissions)
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var role = Role.Create($"Role {Guid.NewGuid():N}", permissions).Value;
        context.Roles.Add(role);
        await context.SaveChangesAsync(Cancellation);
        return role.Id;
    }

    private async Task<Guid> EnsureSystemRoleAsync()
    {
        await using var scope = api.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        if (await context.Roles.FirstOrDefaultAsync(role => role.IsSystem, Cancellation) is { } existing)
        {
            return existing.Id;
        }

        var role = Role.CreateAdministrator();
        context.Roles.Add(role);
        await context.SaveChangesAsync(Cancellation);
        return role.Id;
    }

    private async Task<AuditLogEntry> LastAuditEntryAsync(Guid targetId)
        => await LastAuditEntryOrNullAsync(targetId) ?? throw new InvalidOperationException($"No audit entry for {targetId}.");

    private async Task<AuditLogEntry?> LastAuditEntryOrNullAsync(Guid targetId)
        => (await AuditActionsAsync(targetId)).LastOrDefault();

    private async Task<List<AuditLogEntry>> AuditActionsAsync(Guid targetId)
    {
        await using var scope = api.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().AuditLogEntries.AsNoTracking()
            .Where(entry => entry.TargetId == targetId)
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(Cancellation);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancellation)}");
        return (await response.Content.ReadFromJsonAsync<T>(Cancellation))!;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await JsonAsync(response)).GetProperty("code").GetString();
}
