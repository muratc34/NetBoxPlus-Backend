using System.Formats.Asn1;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text.Json;
using Auth.Application.Abstractions;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security.Passkeys;

/// <summary>Where passkeys belong: the relying party and the browser origins allowed to use them.</summary>
public sealed class PasskeyOptions
{
    public const string SectionName = "Auth:Passkeys";

    /// <summary>The site's domain the passkeys are bound to, such as <c>netboxplus.example</c>; <c>localhost</c> locally.</summary>
    public string RelyingPartyId { get; set; } = string.Empty;

    /// <summary>What the browser shows the user when it asks for a passkey.</summary>
    public string RelyingPartyName { get; set; } = "NetBoxPlus";

    /// <summary>The web app's and the admin panel's origins, such as <c>https://www.netboxplus.example</c>.</summary>
    public IList<string> Origins { get; } = [];
}

internal sealed class PasskeyOptionsValidator : IValidateOptions<PasskeyOptions>
{
    public ValidateOptionsResult Validate(string? name, PasskeyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RelyingPartyId))
        {
            return ValidateOptionsResult.Fail($"{PasskeyOptions.SectionName}:RelyingPartyId is required.");
        }

        if (options.Origins.Count == 0 || options.Origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/"))
        {
            return ValidateOptionsResult.Fail($"{PasskeyOptions.SectionName}:Origins needs at least one origin, each a scheme and host only.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// The WebAuthn ceremonies with Fido2NetLib. Passkeys are discoverable (the sign-in asks for no user name) and
/// require user verification (a PIN or biometric on the device). No attestation is asked for: which make of
/// authenticator it is does not matter here.
/// </summary>
internal sealed class Fido2PasskeyCeremonies(IOptions<PasskeyOptions> options) : IPasskeyCeremonies
{
    private readonly Fido2 _fido2 = new(
        new Fido2Configuration
        {
            RPID = options.Value.RelyingPartyId,
            RPName = options.Value.RelyingPartyName,
            Origins = options.Value.Origins.ToHashSet(StringComparer.Ordinal),
        },
        null!);

    public string RegistrationOptions(Guid userId, string userName, IReadOnlyList<byte[]> existingCredentialIds)
        => _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = UserHandle(userId), Name = userName, DisplayName = userName },
            ExcludeCredentials = [.. existingCredentialIds.Select(id => new PublicKeyCredentialDescriptor(id))],
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        }).ToJson();

    public async Task<NewPasskeyCredential?> VerifyRegistrationAsync(string optionsJson, string credentialJson, CancellationToken cancellationToken)
    {
        try
        {
            var credential = await _fido2.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(credentialJson)!,
                    OriginalOptions = CredentialCreateOptions.FromJson(optionsJson),
                    // Checked by the caller against every account: a credential id names one passkey.
                    IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true),
                },
                cancellationToken).ConfigureAwait(false);

            return new NewPasskeyCredential(credential.Id, credential.PublicKey, credential.SignCount);
        }
        catch (Exception exception) when (IsRejection(exception))
        {
            return null;
        }
    }

    public string SignInOptions()
        => _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        }).ToJson();

    public byte[]? CredentialIdOf(string credentialJson)
    {
        try
        {
            return JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(credentialJson)?.RawId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<long?> VerifySignInAsync(string optionsJson, string credentialJson, StoredPasskey passkey, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var handle = UserHandle(userId);
            var result = await _fido2.MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(credentialJson)!,
                    OriginalOptions = AssertionOptions.FromJson(optionsJson),
                    StoredPublicKey = passkey.PublicKey,
                    StoredSignatureCounter = (uint)passkey.SignCount,
                    IsUserHandleOwnerOfCredentialIdCallback = (owner, _) => Task.FromResult(
                        owner.UserHandle.AsSpan().SequenceEqual(handle) && owner.CredentialId.AsSpan().SequenceEqual(passkey.CredentialId)),
                },
                cancellationToken).ConfigureAwait(false);

            // Fido2NetLib refuses a counter that did not count up on an authenticator that counts: the
            // credential was copied.
            return result.SignCount;
        }
        catch (Exception exception) when (IsRejection(exception))
        {
            return null;
        }
    }

    /// <summary>The WebAuthn user handle: the account's id, 16 bytes; never the address.</summary>
    private static byte[] UserHandle(Guid userId) => userId.ToByteArray();

    /// <summary>What a malformed or forged answer ends in: a refusal, not a server error.</summary>
    private static bool IsRejection(Exception exception)
        => exception is Fido2VerificationException or JsonException or CryptographicException or AsnContentException
            or CborContentException or FormatException or ArgumentException or InvalidOperationException;
}
