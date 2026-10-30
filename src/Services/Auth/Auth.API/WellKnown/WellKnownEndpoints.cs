using System.Text.Json.Serialization;
using Auth.Infrastructure.Security.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Auth.API.WellKnown;

/// <summary>
/// What services need to validate access tokens: a minimal OpenID discovery document (issuer and
/// JWKS address) and the public signing keys. JwtBearer finds both through its <c>Authority</c>.
/// </summary>
internal static class WellKnownEndpoints
{
    public const string DiscoveryPath = "/.well-known/openid-configuration";
    public const string JwksPath = "/.well-known/jwks.json";

    /// <summary>
    /// Services fetch the keys again on their own when a token names an unknown key id; this only
    /// bounds how long an HTTP cache may keep the document.
    /// </summary>
    private static readonly TimeSpan JwksMaxAge = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapWellKnownEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty).ExcludeFromDescription();

        group.MapGet(DiscoveryPath, (HttpRequest request, [FromServices] IOptions<AccessTokenOptions> options) =>
        {
            // The JWKS address follows the host the caller used, so it works inside and outside containers.
            var jwksUri = $"{request.Scheme}://{request.Host}{request.PathBase}{JwksPath}";
            return TypedResults.Ok(new DiscoveryDocument(options.Value.Issuer, jwksUri));
        });

        group.MapGet(JwksPath, (HttpContext context, [FromServices] SigningKeyRing keys) =>
        {
            context.Response.Headers[HeaderNames.CacheControl] = $"public, max-age={(int)JwksMaxAge.TotalSeconds}";
            return TypedResults.Ok(new JsonWebKeySet(
                [.. keys.PublicKeys.Select(key => new JsonWebKey("EC", "sig", SigningKeyRing.Algorithm, key.KeyId, SigningKeyRing.CurveName, key.X, key.Y))]));
        });

        return endpoints;
    }

    internal sealed record DiscoveryDocument(
        [property: JsonPropertyName("issuer")] string Issuer,
        [property: JsonPropertyName("jwks_uri")] string JwksUri);

    internal sealed record JsonWebKeySet([property: JsonPropertyName("keys")] IReadOnlyList<JsonWebKey> Keys);

    /// <summary>A public EC key as in RFC 7517/7518; no private parameter is ever part of it.</summary>
    internal sealed record JsonWebKey(
        [property: JsonPropertyName("kty")] string KeyType,
        [property: JsonPropertyName("use")] string Use,
        [property: JsonPropertyName("alg")] string Algorithm,
        [property: JsonPropertyName("kid")] string KeyId,
        [property: JsonPropertyName("crv")] string Curve,
        [property: JsonPropertyName("x")] string X,
        [property: JsonPropertyName("y")] string Y);
}
