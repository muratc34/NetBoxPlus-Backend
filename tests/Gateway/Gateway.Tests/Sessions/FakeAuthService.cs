using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Tests.Sessions;

/// <summary>
/// Stands in for the auth service on a real port. It knows one password, rotates refresh tokens
/// like the real one (a second use is refused) and counts what it was asked.
/// </summary>
public sealed class FakeAuthService : IAsyncDisposable
{
    public const string Password = "correct horse battery staple";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, Account> _refreshTokens = new(StringComparer.Ordinal);
    private readonly SigningCredentials _signing = new(new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)), SecurityAlgorithms.EcdsaSha256);
    private int _refreshCalls;

    private FakeAuthService(WebApplication app, TimeProvider time)
    {
        _app = app;
        Time = time;
    }

    public string Address { get; private set; } = string.Empty;

    public TimeProvider Time { get; }

    /// <summary>Lifetime of access tokens from sign-in; under the gateway's refresh margin, every request refreshes.</summary>
    public TimeSpan SignInAccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshedAccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Held before answering a refresh, so that concurrent requests overlap.</summary>
    public TimeSpan RefreshDelay { get; set; }

    /// <summary>Every refresh is refused, as when the account was disabled.</summary>
    public bool RefuseRefresh { get; set; }

    public int RefreshCalls => _refreshCalls;

    /// <summary>Refresh tokens presented a second time: what the real service treats as theft.</summary>
    public ConcurrentQueue<string> Reused { get; } = new();

    public ConcurrentQueue<string> SignedOut { get; } = new();

    /// <summary>Bearer tokens that came with password changes.</summary>
    public ConcurrentQueue<string> PasswordChanges { get; } = new();

    public static async Task<FakeAuthService> StartAsync(TimeProvider time)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var service = new FakeAuthService(app, time);

        app.MapPost("/register", (Credentials request) => request.Email?.Contains("taken", StringComparison.Ordinal) == true
            ? Problem(409, "User.EmailTaken")
            : Results.Created((string?)null, new { id = Guid.CreateVersion7() }));
        app.MapPost("/sign-in", (Credentials request) => service.SignIn(request, admin: false));
        app.MapPost("/sign-in/admin", (Credentials request) => service.SignIn(request, admin: true));
        app.MapPost("/refresh", (RefreshRequest request) => service.RefreshAsync(request.RefreshToken));
        app.MapPost("/sign-out", (RefreshRequest request) =>
        {
            service.SignedOut.Enqueue(request.RefreshToken);
            return Results.NoContent();
        });
        app.MapPost("/me/password", (HttpContext context, PasswordRequest request) =>
        {
            var bearer = context.Request.Headers.Authorization.ToString()["Bearer ".Length..];
            service.PasswordChanges.Enqueue(bearer);
            return request.CurrentPassword == Password
                ? Results.Ok(service.Issue(new Account(new JsonWebToken(bearer).Subject, "customer@example.com", false), service.RefreshedAccessTokenLifetime))
                : Problem(400, "User.PasswordIncorrect");
        });

        await app.StartAsync();
        service.Address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        return service;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private IResult SignIn(Credentials request, bool admin)
        => request.Password == Password && request.Email is not null
            ? Results.Ok(Issue(new Account(Guid.CreateVersion7().ToString(), request.Email, admin), SignInAccessTokenLifetime))
            : Problem(401, "User.InvalidCredentials");

    private async Task<IResult> RefreshAsync(string refreshToken)
    {
        Interlocked.Increment(ref _refreshCalls);
        if (RefreshDelay > TimeSpan.Zero)
        {
            await Task.Delay(RefreshDelay);
        }

        if (!_refreshTokens.TryRemove(refreshToken, out var account))
        {
            Reused.Enqueue(refreshToken);
            return Problem(401, "RefreshToken.Invalid");
        }

        return RefuseRefresh ? Problem(401, "RefreshToken.Invalid") : Results.Ok(Issue(account, RefreshedAccessTokenLifetime));
    }

    private object Issue(Account account, TimeSpan accessTokenLifetime)
    {
        var now = Time.GetUtcNow();
        var claims = new List<Claim> { new("sub", account.UserId), new("email", account.Email), new("jti", Guid.NewGuid().ToString()) };
        claims.Add(new Claim("role", account.Admin ? "Administrator" : "Customer"));
        claims.Add(new Claim("permission", account.Admin ? "auth:users:read" : "catalog:movies:watch"));

        var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + accessTokenLifetime).UtcDateTime,
            SigningCredentials = _signing,
        });

        var refreshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _refreshTokens[refreshToken] = account;

        return new
        {
            accessToken,
            accessTokenExpiresAt = now + accessTokenLifetime,
            refreshToken,
            refreshTokenExpiresAt = now + TimeSpan.FromDays(7),
        };
    }

    private static IResult Problem(int status, string code)
        => Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record Account(string UserId, string Email, bool Admin);

    public sealed record Credentials(string? Email, string? Password);

    public sealed record RefreshRequest(string RefreshToken);

    public sealed record PasswordRequest(string? CurrentPassword, string? NewPassword);
}
