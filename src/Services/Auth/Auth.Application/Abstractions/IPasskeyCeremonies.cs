namespace Auth.Application.Abstractions;

/// <summary>
/// The WebAuthn ceremonies: the options a browser gets, and checking the authenticator's answer to them. Options
/// and answers travel as the JSON the browser's <c>PublicKeyCredential</c> uses; nothing of the WebAuthn library
/// leaves the implementation.
/// </summary>
public interface IPasskeyCeremonies
{
    /// <summary>Options for creating a discoverable passkey for the user, none of <paramref name="existingCredentialIds"/> again.</summary>
    string RegistrationOptions(Guid userId, string userName, IReadOnlyList<byte[]> existingCredentialIds);

    /// <summary>
    /// The new credential, if <paramref name="credentialJson"/> answers <paramref name="optionsJson"/> correctly
    /// (challenge, origin, relying party, signature); <see langword="null"/> otherwise.
    /// </summary>
    Task<NewPasskeyCredential?> VerifyRegistrationAsync(string optionsJson, string credentialJson, CancellationToken cancellationToken);

    /// <summary>Options for signing in with any of the site's passkeys; the authenticator says whose.</summary>
    string SignInOptions();

    /// <summary>The credential id an answer names, to find the stored passkey; <see langword="null"/> when it is not readable.</summary>
    byte[]? CredentialIdOf(string credentialJson);

    /// <summary>
    /// The authenticator's new signature counter, if <paramref name="credentialJson"/> answers
    /// <paramref name="optionsJson"/> correctly with the stored passkey of <paramref name="userId"/> (its user handle
    /// must be that user); <see langword="null"/> otherwise, including a counter that went back.
    /// </summary>
    Task<long?> VerifySignInAsync(
        string optionsJson,
        string credentialJson,
        StoredPasskey passkey,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record NewPasskeyCredential(byte[] CredentialId, byte[] PublicKey, long SignCount);

public sealed record StoredPasskey(byte[] CredentialId, byte[] PublicKey, long SignCount);
