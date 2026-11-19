using Microsoft.IdentityModel.Tokens;

namespace Shared.Infrastructure.API.Security;

/// <summary>What every service requires of an access token, besides issuer, audience and lifetime.</summary>
public static class AccessTokenFormat
{
    /// <summary>The <c>typ</c> header of an access token (RFC 9068).</summary>
    public const string Type = "at+jwt";

    /// <summary>ECDSA with P-256 and SHA-256; no other algorithm is accepted.</summary>
    public const string Algorithm = SecurityAlgorithms.EcdsaSha256;
}
