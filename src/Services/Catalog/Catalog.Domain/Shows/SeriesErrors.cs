using Shared.Domain;

namespace Catalog.Domain.Shows;

public static class SeriesErrors
{
    public static readonly Error NotFound = Error.NotFound("Series.NotFound", "The series was not found.");

    public static readonly Error SeasonNotFound = Error.NotFound("Series.SeasonNotFound", "The season was not found.");

    public static readonly Error EpisodeNotFound = Error.NotFound("Series.EpisodeNotFound", "The episode was not found.");

    public static readonly Error SeasonNumberOutOfRange = Error.Validation(
        "Series.SeasonNumberOutOfRange",
        $"The season number must be between 1 and {Season.MaxNumber}.");

    public static readonly Error SeasonTitleTooLong = Error.Validation(
        "Series.SeasonTitleTooLong",
        $"The season title must be at most {Season.TitleMaxLength} characters.");

    public static readonly Error SeasonNumberTaken = Error.Conflict("Series.SeasonNumberTaken", "The series already has a season with this number.");

    public static readonly Error SeasonHasPublishedEpisodes = Error.Conflict(
        "Series.SeasonHasPublishedEpisodes",
        "A season with published episodes cannot be removed; unpublish them first.");

    public static readonly Error EpisodeNumberOutOfRange = Error.Validation(
        "Series.EpisodeNumberOutOfRange",
        $"The episode number must be between 1 and {Episode.MaxNumber}.");

    public static readonly Error EpisodeNumberTaken = Error.Conflict("Series.EpisodeNumberTaken", "The season already has an episode with this number.");

    public static readonly Error EpisodeIsPublished = Error.Conflict(
        "Series.EpisodeIsPublished",
        "A published episode cannot be removed; unpublish it first.");

    public static readonly Error PublishedEpisodeRequired = Error.Conflict(
        "Series.PublishedEpisodeRequired",
        "A published series needs at least one published episode.");
}
