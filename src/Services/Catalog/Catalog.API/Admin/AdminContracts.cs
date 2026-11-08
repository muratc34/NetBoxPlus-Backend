using Catalog.Application.Content;
using Catalog.Domain.Content;

namespace Catalog.API.Admin;

/// <summary>The same strings the auth service's permission catalog holds.</summary>
internal static class CatalogPermissions
{
    public const string MoviesRead = "catalog:movies:read";
    public const string MoviesWrite = "catalog:movies:write";
    public const string MoviesPublish = "catalog:movies:publish";
    public const string MoviesDelete = "catalog:movies:delete";
    public const string SeriesRead = "catalog:series:read";
    public const string SeriesWrite = "catalog:series:write";
    public const string SeriesPublish = "catalog:series:publish";
    public const string SeriesDelete = "catalog:series:delete";
    public const string PeopleRead = "catalog:people:read";
    public const string PeopleWrite = "catalog:people:write";
    public const string GenresRead = "catalog:genres:read";
    public const string GenresWrite = "catalog:genres:write";
    public const string ImagesUpload = "catalog:images:upload";
}

public sealed record CreatedResponse(Guid Id);

/// <param name="Version">On a change: the version that was loaded. Ignored when creating.</param>
public sealed record MovieRequest(
    string? Title,
    string? OriginalTitle,
    string? Synopsis,
    int ReleaseYear,
    AgeRating AgeRating,
    int RuntimeMinutes,
    IReadOnlyList<Guid>? GenreIds,
    IReadOnlyList<CreditInput>? Credits,
    int Version)
{
    internal ContentFields Fields => new(Title, OriginalTitle, Synopsis, ReleaseYear, AgeRating, GenreIds ?? [], Credits ?? []);
}

/// <param name="Version">On a change: the version that was loaded. Ignored when creating.</param>
public sealed record SeriesRequest(
    string? Title,
    string? OriginalTitle,
    string? Synopsis,
    int ReleaseYear,
    AgeRating AgeRating,
    IReadOnlyList<Guid>? GenreIds,
    IReadOnlyList<CreditInput>? Credits,
    int Version)
{
    internal ContentFields Fields => new(Title, OriginalTitle, Synopsis, ReleaseYear, AgeRating, GenreIds ?? [], Credits ?? []);
}

public sealed record SeasonRequest(int Number, string? Title, int Version);

public sealed record EpisodeRequest(int Number, string? Title, string? Synopsis, int RuntimeMinutes, int Version);

public sealed record PersonRequest(string? Name, string? Biography);

public sealed record GenreRequest(string? Name);

/// <param name="Version">The version that was loaded.</param>
public sealed record AttachImageRequest(Guid UploadId, int Version);

public sealed record AttachPhotoRequest(Guid UploadId);
