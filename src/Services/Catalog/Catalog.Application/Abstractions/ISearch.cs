using Catalog.Domain.Content;

namespace Catalog.Application.Abstractions;

/// <summary>A movie or series as the search index holds it.</summary>
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
    IReadOnlyList<string> People);

public sealed record SearchDocumentGenre(Guid Id, IReadOnlyDictionary<string, string> Names);

public sealed record SearchQuery(string Text, string Language, ContentKind? Kind, Guid? GenreId, int Offset, int Size);

public sealed record SearchResults(long Total, IReadOnlyList<SearchDocument> Items);

/// <summary>Reads from the search index. Fails with <see cref="SearchUnavailableException"/> when it cannot answer.</summary>
public interface ISearchIndex
{
    Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchDocument>> SuggestAsync(string text, string language, int size, CancellationToken cancellationToken);
}

public sealed class SearchUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Notes that a title must be brought up to date in the search index; saved with the change itself.</summary>
public interface ISearchQueue
{
    void Enqueue(Guid contentId, ContentKind kind);
}
