using System.Security.Cryptography;
using Auth.Infrastructure.Security.Tokens;
using Microsoft.Extensions.Options;

namespace Auth.IntegrationTests.Tokens;

/// <summary>Fresh keys for every test run; none of them is ever stored.</summary>
internal static class TestSigningKeys
{
    public static string NewPem(ECCurve? curve = null)
    {
        using var key = ECDsa.Create(curve ?? ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    public static AccessTokenOptions Options(string activeKeyId, params (string KeyId, string Pem)[] keys)
    {
        var options = new AccessTokenOptions { ActiveKeyId = activeKeyId };
        foreach (var (keyId, pem) in keys)
        {
            options.SigningKeys[keyId] = pem;
        }

        return options;
    }

    public static SigningKeyRing Ring(AccessTokenOptions options) => new(Microsoft.Extensions.Options.Options.Create(options));
}
