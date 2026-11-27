namespace Auth.Domain.Users;

/// <summary>
/// A locked profile opened with its PIN in one sign-in (refresh token family). Every access token of that sign-in
/// names the profile in its <c>profile_unlocked</c> claim, until the PIN changes or the sign-in ends.
/// </summary>
public sealed class ProfileUnlock
{
    private ProfileUnlock(Guid sessionId, Guid profileId, DateTimeOffset unlockedAt)
    {
        SessionId = sessionId;
        ProfileId = profileId;
        UnlockedAt = unlockedAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private ProfileUnlock()
    {
    }

    /// <summary>The refresh token family.</summary>
    public Guid SessionId { get; private set; }

    public Guid ProfileId { get; private set; }

    public DateTimeOffset UnlockedAt { get; private set; }

    public static ProfileUnlock Create(Guid sessionId, Guid profileId, DateTimeOffset unlockedAt) => new(sessionId, profileId, unlockedAt);
}

public interface IProfileUnlockRepository
{
    Task<bool> ExistsAsync(Guid sessionId, Guid profileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ProfileIdsAsync(Guid sessionId, CancellationToken cancellationToken);

    void Add(ProfileUnlock unlock);

    /// <summary>Forgets every sign-in that opened the profile; saved with the command.</summary>
    Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken);
}
