namespace NetBoxPlus.DemoSeeder.Gateway;

// The parts of the admin API's responses the seeder reads. Its own copies: the seeder is a client
// of the gateway like the admin panel, not a part of any service. Enums stay strings.

internal sealed record Created(Guid Id);

internal sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount);

internal sealed record GenreItem(Guid Id, string Name, IReadOnlyDictionary<string, string> Translations);

internal sealed record PersonItem(Guid Id, string Name, string? PhotoUrl);

internal sealed record TitleItem(Guid Id, string Title, string Status);

internal sealed record MovieItem(Guid Id, string? OriginalTitle, string Status, int Version, string? PosterUrl, string? BackdropUrl, bool HasPlayableVideo);

internal sealed record SeriesItem(Guid Id, string? OriginalTitle, string Status, int Version, string? PosterUrl, string? BackdropUrl, IReadOnlyList<SeasonItem> Seasons);

internal sealed record SeasonItem(Guid Id, int Number, IReadOnlyList<EpisodeItem> Episodes);

internal sealed record EpisodeItem(Guid Id, int Number, string Status, bool HasPlayableVideo);

internal sealed record ImageUploadTicket(Guid UploadId, Uri UploadAddress);

internal sealed record VideoUploadTicket(Guid VideoId, Uri UploadAddress);

internal sealed record VideoItem(Guid Id, string Status, DateTimeOffset CreatedAt);

internal sealed record VideoDetails(Guid Id, string Status, string? FailureReason);

internal sealed record PlanItem(Guid Id, bool IsActive);

internal sealed record PlanDetails(Guid Id, string ProviderPriceId, bool IsActive);

/// <summary>A published status as the API spells it.</summary>
internal static class Statuses
{
    public const string Published = "Published";
}
