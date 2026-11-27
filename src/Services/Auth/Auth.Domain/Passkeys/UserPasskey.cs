using Shared.Domain;

namespace Auth.Domain.Passkeys;

/// <summary>
/// A passkey (WebAuthn credential) the user registered: signing in with it needs no password. Only what verifying
/// a signature needs is kept: the credential's id and public key, and its signature counter.
/// </summary>
public sealed class UserPasskey : AggregateRoot
{
    /// <summary>The most passkeys an account keeps.</summary>
    public const int MaxPerUser = 10;

    public const int MaxNameLength = 50;

    /// <summary>Credential ids are at most 1023 bytes (WebAuthn Level 3).</summary>
    public const int MaxCredentialIdLength = 1023;

    private UserPasskey(Guid id, Guid userId, byte[] credentialId, byte[] publicKey, long signCount, string name, DateTimeOffset createdAt)
        : base(id)
    {
        UserId = userId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        SignCount = signCount;
        Name = name;
        CreatedAt = createdAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private UserPasskey()
    {
    }

    public Guid UserId { get; private set; }

    public byte[] CredentialId { get; private set; } = [];

    /// <summary>The credential's public key, COSE-encoded as the authenticator gave it.</summary>
    public byte[] PublicKey { get; private set; } = [];

    /// <summary>
    /// The authenticator's signature counter at the last use. Many passkeys (synced ones) always report 0; one that
    /// counts must count up, or the credential was copied.
    /// </summary>
    public long SignCount { get; private set; }

    /// <summary>What the user calls it, such as "iPhone" or "Work laptop".</summary>
    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static Result<UserPasskey> Register(Guid userId, byte[] credentialId, byte[] publicKey, long signCount, string name, DateTimeOffset now)
    {
        Ensure.NotEmpty(userId);
        ArgumentNullException.ThrowIfNull(credentialId);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (credentialId.Length is 0 or > MaxCredentialIdLength || publicKey.Length == 0)
        {
            return PasskeyErrors.Invalid;
        }

        if (CleanName(name) is not { } cleaned)
        {
            return PasskeyErrors.NameInvalid;
        }

        var passkey = new UserPasskey(Guid.CreateVersion7(), userId, credentialId, publicKey, signCount, cleaned, now);
        passkey.RaiseDomainEvent(new PasskeyAddedDomainEvent(passkey.Id, userId, cleaned, now));
        return passkey;
    }

    public Result Rename(string name)
    {
        if (CleanName(name) is not { } cleaned)
        {
            return PasskeyErrors.NameInvalid;
        }

        Name = cleaned;
        return Result.Success();
    }

    /// <summary>Records a sign-in with the passkey and the counter it reported.</summary>
    public void RecordUse(long signCount, DateTimeOffset now)
    {
        SignCount = signCount;
        LastUsedAt = now;
    }

    /// <summary>Called before the passkey is deleted, so the user is told.</summary>
    public void Remove(DateTimeOffset now) => RaiseDomainEvent(new PasskeyRemovedDomainEvent(Id, UserId, Name, now));

    /// <summary>A name as it is kept: trimmed, 1 to <see cref="MaxNameLength"/> characters; <see langword="null"/> when it is not valid.</summary>
    public static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        return trimmed is { Length: > 0 and <= MaxNameLength } ? trimmed : null;
    }
}

public sealed record PasskeyAddedDomainEvent(Guid PasskeyId, Guid UserId, string Name, DateTimeOffset AddedAt) : IDomainEvent;

public sealed record PasskeyRemovedDomainEvent(Guid PasskeyId, Guid UserId, string Name, DateTimeOffset RemovedAt) : IDomainEvent;

/// <summary>What a <see cref="PasskeyChallenge"/> was issued for.</summary>
public enum PasskeyCeremony
{
    Registration,
    SignIn,
}

/// <summary>
/// The options sent to the browser for one WebAuthn ceremony, kept to check its answer: it carries the random
/// challenge the authenticator signs. Used once, within <see cref="Lifetime"/>.
/// </summary>
public sealed class PasskeyChallenge : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private PasskeyChallenge(Guid id, Guid? userId, PasskeyCeremony ceremony, string options, DateTimeOffset createdAt)
        : base(id)
    {
        UserId = userId;
        Ceremony = ceremony;
        Options = options;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PasskeyChallenge()
    {
    }

    /// <summary>The user registering a passkey; none for a sign-in, where the passkey names the user.</summary>
    public Guid? UserId { get; private set; }

    public PasskeyCeremony Ceremony { get; private set; }

    /// <summary>The options as JSON, exactly as the browser got them.</summary>
    public string Options { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static PasskeyChallenge ForRegistration(Guid userId, string options, DateTimeOffset now)
    {
        Ensure.NotEmpty(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options);
        return new PasskeyChallenge(Guid.CreateVersion7(), userId, PasskeyCeremony.Registration, options, now);
    }

    public static PasskeyChallenge ForSignIn(string options, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options);
        return new PasskeyChallenge(Guid.CreateVersion7(), null, PasskeyCeremony.SignIn, options, now);
    }
}

public static class PasskeyErrors
{
    /// <summary>Every failed passkey check answers the same, so it tells nothing about the account or the key.</summary>
    public static readonly Error Invalid = Error.Validation("Passkey.Invalid", "The passkey could not be verified. Try again.");

    public static readonly Error SignInFailed = Error.Unauthorized("Passkey.SignInFailed", "Signing in with the passkey did not work. Try again or use your password.");

    public static readonly Error ChallengeInvalid = Error.Validation("Passkey.ChallengeInvalid", "The request has expired or was used already. Start again.");

    public static readonly Error NameInvalid = Error.Validation("Passkey.NameInvalid", $"Name the passkey in 1 to {UserPasskey.MaxNameLength} characters.");

    public static readonly Error TooMany = Error.Conflict("Passkey.TooMany", $"An account can keep at most {UserPasskey.MaxPerUser} passkeys. Remove one first.");

    public static readonly Error AlreadyRegistered = Error.Conflict("Passkey.AlreadyRegistered", "This passkey is registered already.");

    public static readonly Error NotFound = Error.NotFound("Passkey.NotFound", "The passkey was not found.");
}

public interface IPasskeyRepository
{
    Task<UserPasskey?> GetAsync(Guid userId, Guid passkeyId, CancellationToken cancellationToken);

    Task<UserPasskey?> GetByCredentialIdAsync(byte[] credentialId, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<byte[]>> CredentialIdsAsync(Guid userId, CancellationToken cancellationToken);

    void Add(UserPasskey passkey);

    void Remove(UserPasskey passkey);

    void AddChallenge(PasskeyChallenge challenge);

    /// <summary>
    /// The challenge, marked used at once with atomic SQL, if it was issued for <paramref name="ceremony"/> (and
    /// <paramref name="userId"/>, for a registration), is not expired and was not used. <see langword="null"/>
    /// otherwise. At once and outside the command's transaction: a challenge that was tried stays used even when
    /// the ceremony fails, so its signature cannot be tried again.
    /// </summary>
    Task<PasskeyChallenge?> TryUseChallengeAsync(Guid challengeId, PasskeyCeremony ceremony, Guid? userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Removes, with the change being saved, every passkey of a user whose account was deleted.</summary>
    Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken);
}
