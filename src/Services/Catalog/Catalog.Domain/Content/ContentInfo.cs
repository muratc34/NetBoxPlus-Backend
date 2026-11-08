using Shared.Domain;

namespace Catalog.Domain.Content;

/// <summary>What movies and series both describe about themselves.</summary>
public sealed record ContentInfo
{
    public const int TitleMaxLength = 200;
    public const int SynopsisMaxLength = 2000;
    public const int MinReleaseYear = 1888;
    public const int MaxReleaseYear = 2100;

    private ContentInfo(string title, string? originalTitle, string synopsis, int releaseYear, AgeRating ageRating)
    {
        Title = title;
        OriginalTitle = originalTitle;
        Synopsis = synopsis;
        ReleaseYear = releaseYear;
        AgeRating = ageRating;
    }

    public string Title { get; }

    /// <summary>The title in its original language, when it differs.</summary>
    public string? OriginalTitle { get; }

    public string Synopsis { get; }

    public int ReleaseYear { get; }

    public AgeRating AgeRating { get; }

    public static Result<ContentInfo> Create(string? title, string? originalTitle, string? synopsis, int releaseYear, AgeRating ageRating)
    {
        var problem = Check(title, originalTitle, synopsis, releaseYear, ageRating).FirstOrDefault();
        if (problem.Error is not null)
        {
            return problem.Error;
        }

        return new ContentInfo(
            title!.Trim(),
            string.IsNullOrWhiteSpace(originalTitle) ? null : originalTitle.Trim(),
            synopsis!.Trim(),
            releaseYear,
            ageRating);
    }

    /// <summary>Every problem with the values, with the field it belongs to.</summary>
    public static IEnumerable<(string Field, Error Error)> Check(string? title, string? originalTitle, string? synopsis, int releaseYear, AgeRating ageRating)
    {
        var trimmedTitle = title?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            yield return (nameof(Title), ContentErrors.TitleRequired);
        }
        else if (trimmedTitle.Length > TitleMaxLength)
        {
            yield return (nameof(Title), ContentErrors.TitleTooLong);
        }

        if (originalTitle?.Trim().Length > TitleMaxLength)
        {
            yield return (nameof(OriginalTitle), ContentErrors.OriginalTitleTooLong);
        }

        var trimmedSynopsis = synopsis?.Trim();
        if (string.IsNullOrEmpty(trimmedSynopsis))
        {
            yield return (nameof(Synopsis), ContentErrors.SynopsisRequired);
        }
        else if (trimmedSynopsis.Length > SynopsisMaxLength)
        {
            yield return (nameof(Synopsis), ContentErrors.SynopsisTooLong);
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
