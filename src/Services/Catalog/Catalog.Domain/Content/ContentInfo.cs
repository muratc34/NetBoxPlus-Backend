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
        var trimmedTitle = title?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            return ContentErrors.TitleRequired;
        }

        if (trimmedTitle.Length > TitleMaxLength)
        {
            return ContentErrors.TitleTooLong;
        }

        var trimmedOriginal = string.IsNullOrWhiteSpace(originalTitle) ? null : originalTitle.Trim();
        if (trimmedOriginal?.Length > TitleMaxLength)
        {
            return ContentErrors.OriginalTitleTooLong;
        }

        var trimmedSynopsis = synopsis?.Trim();
        if (string.IsNullOrEmpty(trimmedSynopsis))
        {
            return ContentErrors.SynopsisRequired;
        }

        if (trimmedSynopsis.Length > SynopsisMaxLength)
        {
            return ContentErrors.SynopsisTooLong;
        }

        if (releaseYear is < MinReleaseYear or > MaxReleaseYear)
        {
            return ContentErrors.ReleaseYearOutOfRange;
        }

        if (!Enum.IsDefined(ageRating))
        {
            return ContentErrors.AgeRatingInvalid;
        }

        return new ContentInfo(trimmedTitle, trimmedOriginal, trimmedSynopsis, releaseYear, ageRating);
    }
}
