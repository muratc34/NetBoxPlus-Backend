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
    private int _signInCalls;

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

    public int SignInCalls => _signInCalls;

    /// <summary>A registration that answers 201 without the session, as a broken service might.</summary>
    public bool RegisterWithoutTokens { get; set; }

    /// <summary>Refresh tokens presented a second time: what the real service treats as theft.</summary>
    public ConcurrentQueue<string> Reused { get; } = new();

    public ConcurrentQueue<string> SignedOut { get; } = new();

    /// <summary>Bearer tokens that came with password changes.</summary>
    public ConcurrentQueue<string> PasswordChanges { get; } = new();

    /// <summary>Bearer tokens and new addresses that came with e-mail change requests.</summary>
    public ConcurrentQueue<(string Bearer, string? NewEmail)> EmailChanges { get; } = new();

    /// <summary>Bearer tokens that came with account deletions the fake accepted.</summary>
    public ConcurrentQueue<string> Deletions { get; } = new();

    /// <summary>Addresses for which a password reset was asked.</summary>
    public ConcurrentQueue<string?> PasswordResetRequests { get; } = new();

    /// <summary>The User-Agent each sign-in and registration came with.</summary>
    public ConcurrentQueue<string?> UserAgents { get; } = new();

    /// <summary>The session id of every sign-in and registration, in order.</summary>
    public ConcurrentQueue<Guid> Sessions { get; } = new();

    /// <summary>The sessions ended through <c>DELETE /me/sessions[/{id}]</c>.</summary>
    public ConcurrentQueue<Guid> EndedSessions { get; } = new();

    /// <summary>The token the fake accepts for a password reset.</summary>
    public const string ResetToken = "valid-reset-token";

    public static async Task<FakeAuthService> StartAsync(TimeProvider time)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var service = new FakeAuthService(app, time);

        app.MapPost("/register", (HttpContext context, Credentials request) =>
        {
            service.UserAgents.Enqueue(context.Request.Headers.UserAgent.ToString());
            return service.Register(request);
        });
        app.MapPost("/sign-in", (HttpContext context, Credentials request) =>
        {
            service.UserAgents.Enqueue(context.Request.Headers.UserAgent.ToString());
            return service.SignIn(request, admin: false);
        });
        app.MapPost("/sign-in/admin", (Credentials request) => service.SignIn(request, admin: true));

        // Every session it issued belongs to whoever asks: enough to see the gateway pass the answers on.
        app.MapGet("/me/sessions", (HttpContext context) =>
        {
            var current = SessionOf(context);
            return Results.Ok(service.Sessions.Except(service.EndedSessions).Select(id => new
            {
                id,
                kind = "Customer",
                deviceLabel = "Chrome · Windows",
                signedInAt = service.Time.GetUtcNow(),
                lastActiveAt = service.Time.GetUtcNow(),
                current = id == current,
            }));
        });
        app.MapDelete("/me/sessions/{id:guid}", (Guid id) =>
        {
            if (!service.Sessions.Contains(id) || service.EndedSessions.Contains(id))
            {
                return Problem(404, "Session.NotFound");
            }

            service.EndedSessions.Enqueue(id);
            return Results.NoContent();
        });
        app.MapDelete("/me/sessions", (HttpContext context) =>
        {
            var current = SessionOf(context);
            var others = service.Sessions.Except(service.EndedSessions).Where(id => id != current).ToList();
            foreach (var id in others)
            {
                service.EndedSessions.Enqueue(id);
            }

            return Results.Ok(new { ids = others });
        });
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
                ? Results.Ok(service.Issue(new Account(new JsonWebToken(bearer).Subject, "customer@example.com", false, service.NewSession()), service.RefreshedAccessTokenLifetime))
                : Problem(400, "User.PasswordIncorrect");
        });

        app.MapPost("/me/email", (HttpContext context, EmailRequest request) =>
        {
            service.EmailChanges.Enqueue((context.Request.Headers.Authorization.ToString()["Bearer ".Length..], request.NewEmail));
            return request.Password == Password ? Results.NoContent() : Problem(400, "User.PasswordIncorrect");
        });

        app.MapPost("/me/deletion", (HttpContext context, DeletionRequest request) =>
        {
            if (request.Password != Password)
            {
                return Problem(400, "User.PasswordIncorrect");
            }

            service.Deletions.Enqueue(context.Request.Headers.Authorization.ToString()["Bearer ".Length..]);
            return Results.NoContent();
        });

        app.MapPost("/password-reset", (ResetRequest request) =>
        {
            service.PasswordResetRequests.Enqueue(request.Email);
            return Results.Accepted();
        });
        app.MapPost("/password-reset/confirm", (ResetConfirmRequest request) => request.Token == ResetToken
            ? Results.NoContent()
            : Problem(400, "PasswordReset.TokenInvalid"));

        await app.StartAsync();
        service.Address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        return service;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static Guid? SessionOf(HttpContext context)
        => Guid.TryParse(new JsonWebToken(context.Request.Headers.Authorization.ToString()["Bearer ".Length..]).GetClaim("sid").Value, out var id) ? id : null;

    private IResult SignIn(Credentials request, bool admin)
    {
        Interlocked.Increment(ref _signInCalls);
        return request.Password == Password && request.Email is not null
            ? Results.Ok(Issue(new Account(Guid.CreateVersion7().ToString(), request.Email, admin, NewSession()), SignInAccessTokenLifetime))
            : Problem(401, "User.InvalidCredentials");
    }

    // Like the real service: the account and its first session in one answer.
    private IResult Register(Credentials request)
    {
        if (request.Email?.Contains("taken", StringComparison.Ordinal) == true)
        {
            return Problem(409, "User.EmailTaken");
        }

        var id = Guid.CreateVersion7();
        if (RegisterWithoutTokens)
        {
            return Results.Created((string?)null, new { id });
        }

        var pair = (TokenFields)Issue(new Account(id.ToString(), request.Email!, false, NewSession()), SignInAccessTokenLifetime);
        return Results.Created((string?)null, new { id, pair.AccessToken, pair.AccessTokenExpiresAt, pair.RefreshToken, pair.RefreshTokenExpiresAt, pair.SessionId });
    }

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
        var claims = new List<Claim> { new("sub", account.UserId), new("email", account.Email), new("jti", Guid.NewGuid().ToString()), new("sid", account.SessionId.ToString()) };
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

        return new TokenFields(accessToken, now + accessTokenLifetime, refreshToken, now + TimeSpan.FromDays(7), account.SessionId);
    }

    private static IResult Problem(int status, string code)
        => Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

    private Guid NewSession()
    {
        var id = Guid.CreateVersion7();
        Sessions.Enqueue(id);
        return id;
    }

    private sealed record Account(string UserId, string Email, bool Admin, Guid SessionId);

    private sealed record TokenFields(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, Guid SessionId);

    public sealed record Credentials(string? Email, string? Password);

    public sealed record RefreshRequest(string RefreshToken);

    public sealed record PasswordRequest(string? CurrentPassword, string? NewPassword);

    public sealed record ResetRequest(string? Email);

    public sealed record EmailRequest(string? Password, string? NewEmail);

    public sealed record DeletionRequest(string? Password);

    public sealed record ResetConfirmRequest(string? Token, string? NewPassword);
}
