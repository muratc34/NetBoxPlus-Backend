using Shared.Domain;

namespace Auth.Domain.RefreshTokens;

/// <summary>
/// One refresh token. Tokens are single use: refreshing uses the current token and issues the next
/// one in the same family. Presenting a used token again means it was stolen, and the whole family
/// is revoked. Only a hash of the token value is stored.
/// </summary>
public sealed class RefreshToken : Entity
{
    /// <summary>Length of the SHA-256 hash of the token value.</summary>
    public const int HashLength = 32;

    private RefreshToken(
        Guid id,
        Guid userId,
        Guid familyId,
        SessionKind kind,
        byte[] tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset familyExpiresAt)
        : base(id)
    {
        UserId = userId;
        FamilyId = familyId;
        Kind = kind;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        FamilyExpiresAt = familyExpiresAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private RefreshToken()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Every token issued from one sign-in shares the family id.</summary>
    public Guid FamilyId { get; private set; }

    public SessionKind Kind { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>No token of the family lives past this, however often it is refreshed.</summary>
    public DateTimeOffset FamilyExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The first token of a new family, issued at sign-in.</summary>
    public static RefreshToken Issue(Guid userId, SessionKind kind, byte[] tokenHash, DateTimeOffset now)
    {
        Ensure.NotEmpty(userId);
        CheckHash(tokenHash);

        var lifetime = RefreshTokenLifetime.For(kind);
        var familyExpiresAt = now + lifetime.Family;

        return new RefreshToken(
            Guid.CreateVersion7(),
            userId,
            Guid.CreateVersion7(),
            kind,
            tokenHash,
            now,
            Min(now + lifetime.Token, familyExpiresAt),
            familyExpiresAt);
    }

    /// <summary>
    /// The next token of this family. Marking this token used is the repository's job
    /// (<see cref="IRefreshTokenRepository.TryMarkUsedAsync"/>), atomically, so that two
    /// concurrent refreshes with the same token cannot both succeed.
    /// </summary>
    public RefreshToken Rotate(byte[] tokenHash, DateTimeOffset now)
    {
        CheckHash(tokenHash);

        return new RefreshToken(
            Guid.CreateVersion7(),
            UserId,
            FamilyId,
            Kind,
            tokenHash,
            now,
            Min(now + RefreshTokenLifetime.For(Kind).Token, FamilyExpiresAt),
            FamilyExpiresAt);
    }

    /// <summary>Not used, not revoked and not expired.</summary>
    public bool IsActive(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;

    /// <summary>Used before or revoked: presenting it again is reuse, not a mistake.</summary>
    public bool IsSpent => UsedAt is not null || RevokedAt is not null;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private static void CheckHash(byte[] tokenHash)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        if (tokenHash.Length != HashLength)
        {
            throw new ArgumentException($"A token hash is {HashLength} bytes.", nameof(tokenHash));
        }
    }
}
