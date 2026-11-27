namespace Streaming.Domain.Ratings;

/// <summary>
/// Title ids, the strongest signal first. The catalog never learns about profiles: the web app passes these on.
/// </summary>
/// <param name="Like">Loved, then liked, then recently watched titles (newest first within each); at most <see cref="Tastes.MaxLike"/>.</param>
/// <param name="Unlike">Titles rated "not for me", newest first; at most <see cref="Tastes.MaxUnlike"/>.</param>
/// <param name="Seen">Other titles the profile watched, newest first, to be left out of recommendations; at most <see cref="Tastes.MaxSeen"/>.</param>
public sealed record Taste(IReadOnlyList<Guid> Like, IReadOnlyList<Guid> Unlike, IReadOnlyList<Guid> Seen);

public static class Tastes
{
    public const int MaxLike = 20;

    public const int MaxUnlike = 20;

    public const int MaxSeen = 50;

    /// <summary>
    /// Puts the signals in order. A title rated "not for me" is never liked, even when watched; a title is in
    /// one list only. Only titles that can be watched now are kept: the others are not in the catalog's index.
    /// </summary>
    /// <param name="ratings">The profile's ratings, newest first.</param>
    /// <param name="watched">The titles the profile watched, most recent first.</param>
    /// <param name="playable">The titles that can be watched now.</param>
    public static Taste Order(IReadOnlyList<(Guid TitleId, RatingLevel Level)> ratings, IReadOnlyList<Guid> watched, IReadOnlySet<Guid> playable)
    {
        ArgumentNullException.ThrowIfNull(ratings);
        ArgumentNullException.ThrowIfNull(watched);
        ArgumentNullException.ThrowIfNull(playable);

        var unlike = ratings.Where(rating => rating.Level == RatingLevel.NotForMe).Select(rating => rating.TitleId).Where(playable.Contains).Take(MaxUnlike).ToList();
        var notForMe = ratings.Where(rating => rating.Level == RatingLevel.NotForMe).Select(rating => rating.TitleId).ToHashSet();

        var like = ratings.Where(rating => rating.Level == RatingLevel.Love).Select(rating => rating.TitleId)
            .Concat(ratings.Where(rating => rating.Level == RatingLevel.Like).Select(rating => rating.TitleId))
            .Concat(watched.Where(title => !notForMe.Contains(title)))
            .Where(playable.Contains)
            .Distinct()
            .Take(MaxLike)
            .ToList();

        var seen = watched.Where(playable.Contains).Except(like).Except(unlike).Take(MaxSeen).ToList();
        return new Taste(like, unlike, seen);
    }
}
