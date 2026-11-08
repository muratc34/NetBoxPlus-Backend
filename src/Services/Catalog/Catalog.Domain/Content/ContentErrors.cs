using Shared.Domain;

namespace Catalog.Domain.Content;

public static class ContentErrors
{
    public static readonly Error TitleRequired = Error.Validation("Content.TitleRequired", "The title is required.");

    public static readonly Error TitleTooLong = Error.Validation(
        "Content.TitleTooLong",
        $"The title must be at most {ContentText.TitleMaxLength} characters.");

    public static readonly Error OriginalTitleTooLong = Error.Validation(
        "Content.OriginalTitleTooLong",
        $"The original title must be at most {ContentInfo.OriginalTitleMaxLength} characters.");

    public static readonly Error SynopsisRequired = Error.Validation("Content.SynopsisRequired", "The synopsis is required.");

    public static readonly Error SynopsisTooLong = Error.Validation(
        "Content.SynopsisTooLong",
        $"The synopsis must be at most {ContentText.SynopsisMaxLength} characters.");

    public static readonly Error ReleaseYearOutOfRange = Error.Validation(
        "Content.ReleaseYearOutOfRange",
        $"The release year must be between {ContentInfo.MinReleaseYear} and {ContentInfo.MaxReleaseYear}.");

    public static readonly Error UnsupportedLanguage = Error.Validation("Content.UnsupportedLanguage", "One of the languages is not supported.");

    public static readonly Error DefaultLanguageRequired = Error.Validation(
        "Content.DefaultLanguageRequired",
        $"The text is required in the default language ({Languages.Default}).");

    public static readonly Error AgeRatingInvalid = Error.Validation("Content.AgeRatingInvalid", "The age rating is not valid.");

    public static readonly Error RuntimeOutOfRange = Error.Validation(
        "Content.RuntimeOutOfRange",
        $"The runtime must be between 1 and {Runtime.MaxMinutes} minutes.");

    public static readonly Error CreditDuplicate = Error.Validation(
        "Content.CreditDuplicate",
        "The same person cannot be listed twice in the same role.");

    public static readonly Error CreditRoleInvalid = Error.Validation("Content.CreditRoleInvalid", "The credit role is not valid.");

    public static readonly Error CharacterNameTooLong = Error.Validation(
        "Content.CharacterNameTooLong",
        $"The character name must be at most {Credit.CharacterNameMaxLength} characters.");

    public static readonly Error GenreRequired = Error.Conflict(
        "Content.GenreRequired",
        "Published content needs at least one genre.");

    public static readonly Error UnknownGenre = Error.Validation("Content.UnknownGenre", "One of the genres does not exist.");

    public static readonly Error UnknownPerson = Error.Validation("Content.UnknownPerson", "One of the credited people does not exist.");

    public static readonly Error VersionConflict = Error.Conflict(
        "Content.VersionConflict",
        "Someone else changed this since it was loaded. Reload and try again.");

    public static readonly Error PosterRequired = Error.Conflict(
        "Content.PosterRequired",
        "Published content needs a poster.");

    public static readonly Error MustBeUnpublished = Error.Conflict(
        "Content.MustBeUnpublished",
        "Published content must be unpublished first.");
}
