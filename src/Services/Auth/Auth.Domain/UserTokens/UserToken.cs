using Shared.Domain;

namespace Auth.Domain.UserTokens;

/// <summary>What a <see cref="UserToken"/> lets its holder do.</summary>
public enum UserTokenPurpose
{
    EmailVerification,
}

/// <summary>
/// A single-use token sent to the user by e-mail, such as an e-mail verification link. Only the
/// SHA-256 hash of its value is stored; the value itself leaves in the domain event and reaches the
/// user only through the notification service.
/// </summary>
public sealed class UserToken : AggregateRoot
{
    /// <summary>Length of the SHA-256 hash of the token value.</summary>
    public const int HashLength = 32;

    public static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromHours(24);

    /// <summary>A new e-mail verification token cannot be asked for sooner than this after the last one.</summary>
    public static readonly TimeSpan EmailVerificationCooldown = TimeSpan.FromSeconds(60);

    private UserToken(Guid id, Guid userId, UserTokenPurpose purpose, byte[] tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(id)
    {
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private UserToken()
    {
    }

    public Guid UserId { get; private set; }

    public UserTokenPurpose Purpose { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Set when a newer token of the same purpose replaced it.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// A new e-mail verification token. <paramref name="value"/> is only carried by the domain event,
    /// which hands it on to be e-mailed; it is never stored.
    /// </summary>
    public static UserToken IssueEmailVerification(Guid userId, string value, byte[] tokenHash, DateTimeOffset now)
    {
        Ensure.NotEmpty(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        CheckHash(tokenHash);

        var token = new UserToken(Guid.CreateVersion7(), userId, UserTokenPurpose.EmailVerification, tokenHash, now, now + EmailVerificationLifetime);
        token.RaiseDomainEvent(new EmailVerificationTokenIssuedDomainEvent(userId, token.Id, value, token.ExpiresAt));
        return token;
    }

    /// <summary>Not used, not revoked and not expired.</summary>
    public bool IsActive(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;

    /// <summary>A newer token replaces this one; a used or revoked token stays as it is.</summary>
    public void Revoke(DateTimeOffset now)
    {
        if (UsedAt is null && RevokedAt is null)
        {
            RevokedAt = now;
        }
    }

    private static void CheckHash(byte[] tokenHash)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        if (tokenHash.Length != HashLength)
        {
            throw new ArgumentException($"A token hash is {HashLength} bytes.", nameof(tokenHash));
        }
    }
}

/// <summary>Carries the token value, in memory only, to the handler that publishes it for the e-mail.</summary>
public sealed record EmailVerificationTokenIssuedDomainEvent(Guid UserId, Guid TokenId, string Token, DateTimeOffset ExpiresAt) : IDomainEvent;

public static class UserTokenErrors
{
    /// <summary>
    /// Unknown, expired, used or replaced: the caller cannot tell which, and asks for a new link.
    /// </summary>
    public static readonly Error EmailVerificationInvalid = Error.Validation(
        "EmailVerification.TokenInvalid",
        "The verification link is not valid or has expired. Ask for a new one.");

    public static readonly Error AlreadyVerified = Error.Conflict("EmailVerification.AlreadyVerified", "The e-mail address is already verified.");

    public static readonly Error RequestedRecently = Error.Conflict(
        "EmailVerification.RequestedRecently",
        "A verification e-mail was sent moments ago. Wait a minute before asking again.");
}

/// <summary>
/// Stores user tokens. Using a token runs in the database at once and atomically, so that two
/// requests with the same token cannot both succeed.
/// </summary>
public interface IUserTokenRepository
{
    Task<UserToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>The user's tokens of this purpose that are neither used nor revoked.</summary>
    Task<IReadOnlyList<UserToken>> GetUnusedAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken);

    /// <summary>When the user's newest token of this purpose was issued, or null if none was.</summary>
    Task<DateTimeOffset?> GetLastIssuedAtAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken);

    void Add(UserToken token);

    /// <summary>
    /// Marks the token used if it is still unused and not revoked; one statement, so of two concurrent
    /// requests only one wins. It stays used even if the command then fails, as a used refresh token does.
    /// </summary>
    /// <returns><see langword="false"/> when another request used or revoked it first.</returns>
    Task<bool> TryMarkUsedAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken);
}
