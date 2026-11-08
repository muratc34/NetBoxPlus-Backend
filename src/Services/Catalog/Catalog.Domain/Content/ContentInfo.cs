using Shared.Domain;

namespace Catalog.Domain.Content;

/// <summary>What does not change with the language of a movie or series.</summary>
public sealed record ContentInfo
{
    public const int OriginalTitleMaxLength = 200;
    public const int MinReleaseYear = 1888;
    public const int MaxReleaseYear = 2100;

    private ContentInfo(string? originalTitle, int releaseYear, AgeRating ageRating)
    {
        OriginalTitle = originalTitle;
        ReleaseYear = releaseYear;
        AgeRating = ageRating;
    }

    /// <summary>The title in its original language, when there is one to show.</summary>
    public string? OriginalTitle { get; }

    public int ReleaseYear { get; }

    public AgeRating AgeRating { get; }

    public static Result<ContentInfo> Create(string? originalTitle, int releaseYear, AgeRating ageRating)
    {
        var problem = Check(originalTitle, releaseYear, ageRating).FirstOrDefault();
        return problem.Error is not null
            ? problem.Error
            : new ContentInfo(string.IsNullOrWhiteSpace(originalTitle) ? null : originalTitle.Trim(), releaseYear, ageRating);
    }

    /// <summary>Every problem with the values, with the field it belongs to.</summary>
    public static IEnumerable<(string Field, Error Error)> Check(string? originalTitle, int releaseYear, AgeRating ageRating)
    {
        if (originalTitle?.Trim().Length > OriginalTitleMaxLength)
        {
            yield return (nameof(OriginalTitle), ContentErrors.OriginalTitleTooLong);
        }

        if (releaseYear is < MinReleaseYear or > MaxReleaseYear)
        {
            yield return (nameof(ReleaseYear), ContentErrors.ReleaseYearOutOfRange);
        }

        if (!Enum.IsDefined(ageRating))
        {
            yield return (nameof(AgeRating), ContentErrors.AgeRatingInvalid);
        }
    }
}
