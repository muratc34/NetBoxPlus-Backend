using System.Security.Cryptography;
using Auth.Application.Abstractions;
using Auth.Infrastructure.Security.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth.IntegrationTests.Tokens;

/// <summary>The issuer and the key checks on their own; no host, no database.</summary>
public sealed class AccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly AccessTokenSubject Jane = new(
        Guid.CreateVersion7(),
        "jane@example.com",
        ["Editor"],
        ["auth:users:read", "sample:notes:delete"]);

    [Fact]
    public async Task Token_CarriesTheSubjectRolesAndPermissions()
    {
        var options = TestSigningKeys.Options("key-1", ("key-1", TestSigningKeys.NewPem()));
        using var ring = TestSigningKeys.Ring(options);

        var issued = Issuer(ring, options).Issue(Jane);
        var result = await ValidateAsync(issued.Value, ring, options);

        Assert.True(result.IsValid, result.Exception?.Message);
        var token = (JsonWebToken)result.SecurityToken;
        Assert.Equal("at+jwt", token.Typ);
        Assert.Equal("ES256", token.Alg);
        Assert.Equal("key-1", token.Kid);
        Assert.Equal("netboxplus-auth", token.Issuer);
        Assert.Equal(["netboxplus"], token.Audiences);
        Assert.Equal(Jane.UserId.ToString(), token.Subject);
        Assert.Equal(Jane.Email, token.GetClaim("email").Value);
        Assert.True(Guid.TryParse(token.Id, out _));
        Assert.Equal(["Editor"], token.Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value));
        Assert.Equal(Jane.Permissions, token.Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value));
    }

    [Fact]
    public void Token_LivesFifteenMinutesFromNow()
    {
        var options = TestSigningKeys.Options("key-1", ("key-1", TestSigningKeys.NewPem()));
        using var ring = TestSigningKeys.Ring(options);

        var issued = Issuer(ring, options).Issue(Jane);
        var token = new JsonWebToken(issued.Value);

        Assert.Equal(Now.UtcDateTime, token.IssuedAt);
        Assert.Equal(Now.UtcDateTime, token.ValidFrom);
        Assert.Equal(Now.AddMinutes(15).UtcDateTime, token.ValidTo);
        Assert.Equal(Now.AddMinutes(15), issued.ExpiresAt);
    }

    [Fact]
    public void SingleRoleAndPermission_AreStillArrays()
    {
        var options = TestSigningKeys.Options("key-1", ("key-1", TestSigningKeys.NewPem()));
        using var ring = TestSigningKeys.Ring(options);

        var issued = Issuer(ring, options).Issue(Jane with { Roles = ["Editor"], Permissions = ["auth:users:read"] });
        var payload = Base64UrlEncoder.Decode(issued.Value.Split('.')[1]);

        Assert.Contains("\"role\":[\"Editor\"]", payload, StringComparison.Ordinal);
        Assert.Contains("\"permission\":[\"auth:users:read\"]", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveKeySigns_AndTheOtherKeysStillValidate()
    {
        var oldPem = TestSigningKeys.NewPem();
        var newPem = TestSigningKeys.NewPem();

        var before = TestSigningKeys.Options("old", ("old", oldPem), ("new", newPem));
        using var beforeRing = TestSigningKeys.Ring(before);
        var oldToken = Issuer(beforeRing, before).Issue(Jane).Value;

        var after = TestSigningKeys.Options("new", ("old", oldPem), ("new", newPem));
        using var afterRing = TestSigningKeys.Ring(after);
        var newToken = Issuer(afterRing, after).Issue(Jane).Value;

        Assert.Equal("old", new JsonWebToken(oldToken).Kid);
        Assert.Equal("new", new JsonWebToken(newToken).Kid);
        Assert.True((await ValidateAsync(oldToken, afterRing, after)).IsValid);
        Assert.True((await ValidateAsync(newToken, afterRing, after)).IsValid);

        // Once the old key is removed, its tokens stop validating.
        var removed = TestSigningKeys.Options("new", ("new", newPem));
        using var removedRing = TestSigningKeys.Ring(removed);
        Assert.False((await ValidateAsync(oldToken, removedRing, removed)).IsValid);
    }

    [Fact]
    public void PublicKeys_HoldOnlyThePublicPoint()
    {
        var pem = TestSigningKeys.NewPem();
        var options = TestSigningKeys.Options("key-1", ("key-1", pem));
        using var ring = TestSigningKeys.Ring(options);
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        var expected = key.ExportParameters(includePrivateParameters: false).Q;

        var published = Assert.Single(ring.PublicKeys);

        Assert.Equal("key-1", published.KeyId);
        Assert.Equal(expected.X, Base64UrlEncoder.DecodeBytes(published.X));
        Assert.Equal(expected.Y, Base64UrlEncoder.DecodeBytes(published.Y));
    }

    [Fact]
    public void ValidOptions_PassValidation()
    {
        var options = TestSigningKeys.Options("2026-10-30.a_1", ("2026-10-30.a_1", TestSigningKeys.NewPem()));

        Assert.True(new AccessTokenOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void NoKeys_FailValidation()
    {
        AssertInvalid(TestSigningKeys.Options("key-1"), "has no keys");
    }

    [Fact]
    public void ActiveKeyThatIsNotConfigured_FailsValidation()
    {
        AssertInvalid(TestSigningKeys.Options("key-2", ("key-1", TestSigningKeys.NewPem())), "ActiveKeyId");
    }

    [Fact]
    public void KeyOnAnotherCurve_FailsValidation()
    {
        AssertInvalid(
            TestSigningKeys.Options("key-1", ("key-1", TestSigningKeys.NewPem(ECCurve.NamedCurves.nistP384))),
            "must be a P-256 private key");
    }

    [Fact]
    public void PublicKeyOnly_FailsValidation()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        AssertInvalid(
            TestSigningKeys.Options("key-1", ("key-1", key.ExportSubjectPublicKeyInfoPem())),
            "must be a P-256 private key");
    }

    [Fact]
    public void RsaKey_FailsValidation()
    {
        using var key = RSA.Create(2048);

        AssertInvalid(TestSigningKeys.Options("key-1", ("key-1", key.ExportPkcs8PrivateKeyPem())), "must be a P-256 private key");
    }

    [Fact]
    public void NotPem_FailsValidation_WithoutEchoingTheValue()
    {
        var failure = AssertInvalid(TestSigningKeys.Options("key-1", ("key-1", "not-a-key-value")), "must be a P-256 private key");

        Assert.DoesNotContain("not-a-key-value", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyIdWithOtherCharacters_FailsValidation()
    {
        AssertInvalid(TestSigningKeys.Options("key 1", ("key 1", TestSigningKeys.NewPem())), "Signing key id");
    }

    private static JwtAccessTokenIssuer Issuer(SigningKeyRing ring, AccessTokenOptions options)
        => new(ring, Options.Create(options), new FakeTimeProvider(Now));

    private static Task<TokenValidationResult> ValidateAsync(string token, SigningKeyRing ring, AccessTokenOptions options)
        => new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKeys = ring.ValidationKeys,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidTypes = ["at+jwt"],

            // The tokens are issued at a fixed time in the past.
            ValidateLifetime = false,
        });

    private static string AssertInvalid(AccessTokenOptions options, string expected)
    {
        var result = new AccessTokenOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(expected, result.FailureMessage, StringComparison.Ordinal);
        return result.FailureMessage;
    }
}
