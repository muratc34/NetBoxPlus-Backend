using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Security.Tokens;

/// <summary>
/// The configured signing keys: the active one signs, all of them verify and are published. Keys
/// are checked when the application starts (see <see cref="AccessTokenOptionsValidator"/>).
/// </summary>
public sealed partial class SigningKeyRing : IDisposable
{
    /// <summary>The only algorithm tokens are signed with, and the only one services accept.</summary>
    public const string Algorithm = SecurityAlgorithms.EcdsaSha256;

    public const string CurveName = "P-256";

    private const string P256Oid = "1.2.840.10045.3.1.7";

    private readonly List<ECDsa> _keys = [];

    public SigningKeyRing(IOptions<AccessTokenOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;
        var securityKeys = new List<ECDsaSecurityKey>();
        var publicKeys = new List<PublicSigningKey>();

        foreach (var (keyId, pem) in settings.SigningKeys.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var key = Load(pem) ?? throw new InvalidOperationException($"Signing key '{keyId}' is not a P-256 private key.");
            _keys.Add(key);
            securityKeys.Add(new ECDsaSecurityKey(key) { KeyId = keyId });

            var point = key.ExportParameters(includePrivateParameters: false).Q;
            publicKeys.Add(new PublicSigningKey(keyId, Base64Url.EncodeToString(point.X), Base64Url.EncodeToString(point.Y)));
        }

        var active = securityKeys.SingleOrDefault(key => key.KeyId == settings.ActiveKeyId)
            ?? throw new InvalidOperationException($"Active signing key '{settings.ActiveKeyId}' is not configured.");

        SigningCredentials = new SigningCredentials(active, Algorithm);
        ValidationKeys = securityKeys;
        PublicKeys = publicKeys;
    }

    public SigningCredentials SigningCredentials { get; }

    /// <summary>Every configured key, for validating tokens inside the auth service itself.</summary>
    public IReadOnlyList<SecurityKey> ValidationKeys { get; }

    /// <summary>The public halves, for the JWKS document.</summary>
    public IReadOnlyList<PublicSigningKey> PublicKeys { get; }

    public void Dispose()
    {
        foreach (var key in _keys)
        {
            key.Dispose();
        }
    }

    internal static bool IsValidKeyId(string keyId) => KeyIdPattern().IsMatch(keyId);

    /// <summary>Whether <paramref name="pem"/> holds a P-256 private key.</summary>
    internal static bool IsValidKey(string pem)
    {
        using var key = Load(pem);
        return key is not null;
    }

    private static ECDsa? Load(string pem)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);

            // Throws without the private part: a public key alone cannot sign.
            var curve = key.ExportParameters(includePrivateParameters: true).Curve;
            if (curve.IsNamed && (curve.Oid.Value == P256Oid || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256"))
            {
                return key;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
        }

        key.Dispose();
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex KeyIdPattern();
}

/// <summary>A public P-256 key; <see cref="X"/> and <see cref="Y"/> are base64url as in a JWK.</summary>
public sealed record PublicSigningKey(string KeyId, string X, string Y);
