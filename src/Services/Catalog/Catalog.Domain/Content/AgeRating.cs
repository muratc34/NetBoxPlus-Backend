namespace Catalog.Domain.Content;

/// <summary>The youngest audience a title is meant for.</summary>
public enum AgeRating
{
    All,
    Age7,
    Age13,
    Age16,
    Age18,
}

public static class AgeRatings
{
    /// <summary>
    /// The ratings a viewer of <paramref name="maximum"/> may see, youngest first. Listed rather than
    /// compared: the database keeps the names, and "Age13" sorts before "Age7".
    /// </summary>
    public static AgeRating[] UpTo(AgeRating maximum) => [.. Enum.GetValues<AgeRating>().Where(rating => rating <= maximum)];
}
