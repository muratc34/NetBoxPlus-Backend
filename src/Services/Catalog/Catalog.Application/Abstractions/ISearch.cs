using Catalog.Domain.Content;

namespace Catalog.Application.Abstractions;

/// <summary>A movie or series as the search index holds it.</summary>
/// <param name="Popularity">The title's score in the streaming service's weekly ranking; 0 when it is not in it.</param>
/// <param name="BackdropImage">
/// The landscape image's name. Stored, not searched (the mapping is not dynamic), so adding it changed no mapping;
/// documents indexed before it have none until they are indexed again (<c>Catalog.Migrator -- reindex</c>).
/// </param>
/// <param name="LogoImage">The logo's name; stored, not searched, and added later like <paramref name="BackdropImage"/>.</param>
public sealed record SearchDocument(
    Guid Id,
    ContentKind Kind,
    IReadOnlyDictionary<string, string> Title,
    IReadOnlyDictionary<string, string> Synopsis,
    IReadOnlyDictionary<string, string> Suggest,
    string? OriginalTitle,
    int ReleaseYear,
    AgeRating AgeRating,
    string? PosterImage,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<Guid> GenreIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Genres,
    IReadOnlyList<SearchDocumentGenre> GenreList,
    IReadOnlyList<string> People,
    int Popularity = 0,
    string? BackdropImage = null,
    string? LogoImage = null);

public sealed record SearchDocumentGenre(Guid Id, IReadOnlyDictionary<string, string> Names);

/// <param name="MaxAgeRating">Only titles a viewer of this age may see; all when null.</param>
public sealed record SearchQuery(string Text, string Language, ContentKind? Kind, Guid? GenreId, int Offset, int Size, AgeRating? MaxAgeRating = null);

public sealed record SearchResults(long Total, IReadOnlyList<SearchDocument> Items);

/// <summary>Reads from the search index. Fails with <see cref="SearchUnavailableException"/> when it cannot answer.</summary>
public interface ISearchIndex
{
    Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchDocument>> SuggestAsync(string text, string language, int size, AgeRating? maxAgeRating, CancellationToken cancellationToken);
}

public sealed class SearchUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Notes that a title must be brought up to date in the search index; saved with the change itself.</summary>
public interface ISearchQueue
{
    void Enqueue(Guid contentId, ContentKind kind);
}
