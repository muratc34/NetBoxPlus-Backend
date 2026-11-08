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
    IReadOnlyDictionary<string, ContentTextFields>? Translations,
    string? OriginalTitle,
    int ReleaseYear,
    AgeRating AgeRating,
    int RuntimeMinutes,
    IReadOnlyList<Guid>? GenreIds,
    IReadOnlyList<CreditInput>? Credits,
    int Version)
{
    internal ContentFields Fields => new(Translations ?? new Dictionary<string, ContentTextFields>(), OriginalTitle, ReleaseYear, AgeRating, GenreIds ?? [], Credits ?? []);
}

/// <param name="Version">On a change: the version that was loaded. Ignored when creating.</param>
public sealed record SeriesRequest(
    IReadOnlyDictionary<string, ContentTextFields>? Translations,
    string? OriginalTitle,
    int ReleaseYear,
    AgeRating AgeRating,
    IReadOnlyList<Guid>? GenreIds,
    IReadOnlyList<CreditInput>? Credits,
    int Version)
{
    internal ContentFields Fields => new(Translations ?? new Dictionary<string, ContentTextFields>(), OriginalTitle, ReleaseYear, AgeRating, GenreIds ?? [], Credits ?? []);
}

public sealed record SeasonRequest(int Number, IReadOnlyDictionary<string, string?>? Translations, int Version);

public sealed record EpisodeRequest(int Number, IReadOnlyDictionary<string, ContentTextFields>? Translations, int RuntimeMinutes, int Version)
{
    internal IReadOnlyDictionary<string, ContentTextFields> Texts => Translations ?? new Dictionary<string, ContentTextFields>();
}

/// <param name="Translations">The biography per language.</param>
public sealed record PersonRequest(string? Name, IReadOnlyDictionary<string, string?>? Translations);

/// <param name="Translations">The name per language.</param>
public sealed record GenreRequest(IReadOnlyDictionary<string, string?>? Translations)
{
    internal IReadOnlyDictionary<string, string?> Names => Translations ?? new Dictionary<string, string?>();
}

/// <param name="Version">The version that was loaded.</param>
public sealed record AttachImageRequest(Guid UploadId, int Version);

public sealed record AttachPhotoRequest(Guid UploadId);
