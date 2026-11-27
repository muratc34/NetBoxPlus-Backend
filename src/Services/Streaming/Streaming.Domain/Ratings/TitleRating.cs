using Shared.Domain;

namespace Streaming.Domain.Ratings;

/// <summary>How much a profile cared for a title, in the three steps a viewer is offered.</summary>
public enum RatingLevel
{
    NotForMe,
    Like,
    Love,
}

/// <summary>
/// A profile's rating of a movie or series: one per profile and title, changed in place. It shapes the
/// profile's recommendations; nothing else reads it.
/// </summary>
public sealed class TitleRating
{
    private TitleRating(Guid profileId, Guid titleId, Guid userId, RatingLevel level, DateTimeOffset ratedAt)
    {
        ProfileId = profileId;
        TitleId = titleId;
        UserId = userId;
        Level = level;
        RatedAt = ratedAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private TitleRating()
    {
    }

    public Guid ProfileId { get; private set; }

    public Guid TitleId { get; private set; }

    /// <summary>Whose profile it is: what an account deletion removes.</summary>
    public Guid UserId { get; private set; }

    public RatingLevel Level { get; private set; }

    /// <summary>When the profile last chose the level; the newest ratings weigh most.</summary>
    public DateTimeOffset RatedAt { get; private set; }

    public static TitleRating Create(Guid profileId, Guid userId, Guid titleId, RatingLevel level, DateTimeOffset now)
    {
        Ensure.NotEmpty(profileId);
        Ensure.NotEmpty(userId);
        Ensure.NotEmpty(titleId);
        return new TitleRating(profileId, titleId, userId, level, now);
    }

    /// <summary>Chooses another level; choosing the same one again keeps its time, so it does not count as newer.</summary>
    public void Change(RatingLevel level, DateTimeOffset now)
    {
        if (level == Level)
        {
            return;
        }

        Level = level;
        RatedAt = now;
    }
}

public interface ITitleRatingRepository
{
    Task<TitleRating?> FindAsync(Guid profileId, Guid titleId, CancellationToken cancellationToken);

    void Add(TitleRating rating);

    void Remove(TitleRating rating);

    /// <summary>Removes, with the change being saved, every rating of a profile that no longer exists.</summary>
    Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Removes, with the change being saved, every rating of a user whose account was deleted.</summary>
    Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken);
}
