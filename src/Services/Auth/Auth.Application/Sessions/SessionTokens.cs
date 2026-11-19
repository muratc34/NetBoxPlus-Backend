using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Auth.Application.Abstractions;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Auth.Application.Sessions;

/// <summary>What a session hands the client: a short-lived access token and the refresh token that renews it.</summary>
public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>The role names and the union of their permissions, as they are now.</summary>
internal sealed record Grants(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)
{
    public bool Has(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
}

/// <summary>
/// Issues token pairs. The refresh token is added to the repository, so it is stored with the
/// command's save; the value itself only ever reaches the client.
/// </summary>
internal sealed class SessionTokens(
    IAuthReadModel readModel,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenRepository refreshTokens,
    TimeProvider timeProvider)
{
    /// <summary>256 random bits, the same strength as the SHA-256 hash that is stored.</summary>
    private const int ValueLength = 32;

    /// <summary>The SHA-256 hash under which a refresh token value is stored and looked up.</summary>
    public static byte[] Hash(string refreshToken) => SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));

    public async Task<Grants> GetGrantsAsync(User user, CancellationToken cancellationToken)
    {
        var roleIds = user.Roles.Select(role => role.RoleId).ToList();
        var roles = await readModel.Roles
            .Where(role => roleIds.Contains(role.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Grants(
            [.. roles.Select(role => role.Name).Order(StringComparer.Ordinal)],
            [.. roles.SelectMany(role => role.Permissions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    /// <summary>A new session: the first token of a new family.</summary>
    public TokenPair Start(User user, SessionKind kind, Grants grants)
    {
        var value = NewValue();
        return Complete(user, RefreshToken.Issue(user.Id, kind, Hash(value), timeProvider.GetUtcNow()), value, grants);
    }

    /// <summary>The same session, renewed: the next token of <paramref name="current"/>'s family.</summary>
    public TokenPair Continue(User user, RefreshToken current, Grants grants)
    {
        var value = NewValue();
        return Complete(user, current.Rotate(Hash(value), timeProvider.GetUtcNow()), value, grants);
    }

    private TokenPair Complete(User user, RefreshToken refreshToken, string value, Grants grants)
    {
        refreshTokens.Add(refreshToken);

        var accessToken = accessTokens.Issue(new AccessTokenSubject(user.Id, user.Email.Value, grants.Roles, grants.Permissions));
        return new TokenPair(accessToken.Value, accessToken.ExpiresAt, value, refreshToken.ExpiresAt);
    }

    private static string NewValue() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ValueLength));
}
