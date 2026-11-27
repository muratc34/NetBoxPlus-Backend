using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Auth.IntegrationTests.Passkeys;

/// <summary>
/// A software WebAuthn authenticator: answers creation and request options the way a browser's
/// <c>PublicKeyCredential.toJSON()</c> does, with a real P-256 key, "none" attestation and user verification.
/// </summary>
public sealed class TestAuthenticator : IDisposable
{
    public const string Origin = "http://localhost:5173";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(16);

    /// <summary>The user handle the site gave when the passkey was created; sent back with every signature.</summary>
    public byte[] UserHandle { get; private set; } = [];

    /// <summary>The signature counter: a counting authenticator adds one before every signature; 0 stays 0, as synced passkeys do.</summary>
    public uint Counter { get; set; }

    /// <summary>The <c>PublicKeyCredential</c> JSON answering creation options.</summary>
    public JsonElement Create(JsonElement options, string origin = Origin)
    {
        var rpId = options.GetProperty("rp").GetProperty("id").GetString()!;
        UserHandle = Base64Url.DecodeFromChars(options.GetProperty("user").GetProperty("id").GetString());
        var clientData = ClientData("webauthn.create", options.GetProperty("challenge").GetString()!, origin);

        var parameters = _key.ExportParameters(includePrivateParameters: false);
        var cose = new CborWriter();
        cose.WriteStartMap(5);
        cose.WriteInt32(1);
        cose.WriteInt32(2);
        cose.WriteInt32(3);
        cose.WriteInt32(-7);
        cose.WriteInt32(-1);
        cose.WriteInt32(1);
        cose.WriteInt32(-2);
        cose.WriteByteString(parameters.Q.X!);
        cose.WriteInt32(-3);
        cose.WriteByteString(parameters.Q.Y!);
        cose.WriteEndMap();

        var attested = new List<byte>();
        attested.AddRange(new byte[16]);
        attested.AddRange([(byte)(CredentialId.Length >> 8), (byte)CredentialId.Length]);
        attested.AddRange(CredentialId);
        attested.AddRange(cose.Encode());
        var authenticatorData = AuthenticatorData(rpId, flags: 0x45, attested.ToArray());

        var attestation = new CborWriter();
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authenticatorData);
        attestation.WriteEndMap();

        return Json(new JsonObject
        {
            ["id"] = Base64Url.EncodeToString(CredentialId),
            ["rawId"] = Base64Url.EncodeToString(CredentialId),
            ["type"] = "public-key",
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["attestationObject"] = Base64Url.EncodeToString(attestation.Encode()),
                ["transports"] = new JsonArray("internal"),
            },
            ["clientExtensionResults"] = new JsonObject(),
        });
    }

    /// <summary>The <c>PublicKeyCredential</c> JSON answering request options: a signature over the challenge.</summary>
    public JsonElement Sign(JsonElement options, string origin = Origin)
    {
        var rpId = options.GetProperty("rpId").GetString()!;
        if (Counter > 0)
        {
            Counter++;
        }

        var clientData = ClientData("webauthn.get", options.GetProperty("challenge").GetString()!, origin);
        var authenticatorData = AuthenticatorData(rpId, flags: 0x05, []);
        var signature = _key.SignData([.. authenticatorData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return Json(new JsonObject
        {
            ["id"] = Base64Url.EncodeToString(CredentialId),
            ["rawId"] = Base64Url.EncodeToString(CredentialId),
            ["type"] = "public-key",
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["authenticatorData"] = Base64Url.EncodeToString(authenticatorData),
                ["signature"] = Base64Url.EncodeToString(signature),
                ["userHandle"] = Base64Url.EncodeToString(UserHandle),
            },
            ["clientExtensionResults"] = new JsonObject(),
        });
    }

    public void Dispose() => _key.Dispose();

    private static byte[] ClientData(string type, string challenge, string origin)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin, crossOrigin = false }));

    private byte[] AuthenticatorData(string rpId, byte flags, byte[] attested)
    {
        var counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, Counter);
        return [.. SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), flags, .. counter, .. attested];
    }

    private static JsonElement Json(JsonObject node) => JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
}
