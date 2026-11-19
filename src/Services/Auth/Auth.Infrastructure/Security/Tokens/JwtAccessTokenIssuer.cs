using Auth.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Security.Tokens;

/// <summary>
/// Signs access tokens as JWTs of type <c>at+jwt</c> (RFC 9068) with the active key. Claim names
/// match <c>NetBoxPlusClaimTypes</c> in Shared.Infrastructure.API.
/// </summary>
internal sealed class JwtAccessTokenIssuer(
    SigningKeyRing keys,
    IOptions<AccessTokenOptions> options,
    TimeProvider timeProvider) : IAccessTokenIssuer
{
    /// <summary>Same value as <c>AccessTokenFormat.Type</c>, which services require.</summary>
    public const string TokenType = "at+jwt";

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };
    private readonly AccessTokenOptions _options = options.Value;

    public AccessToken Issue(AccessTokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // Whole seconds, as the token stores them, so ExpiresAt matches the exp claim exactly.
        var now = DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().ToUnixTimeSeconds());
        var expiresAt = now + _options.AccessTokenLifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = TokenType,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = keys.SigningCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = subject.UserId.ToString(),
                [JwtRegisteredClaimNames.Email] = subject.Email,
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
                ["role"] = subject.Roles.ToArray(),
                ["permission"] = subject.Permissions.ToArray(),
            },
        });

        return new AccessToken(token, expiresAt);
    }
}
