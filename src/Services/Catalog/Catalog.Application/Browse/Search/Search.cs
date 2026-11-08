using Catalog.Application.Abstractions;
using Catalog.Application.Browse.GetTitles;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Browse.Search;

public static class SearchErrors
{
    public const int MaxQueryLength = 200;

    /// <summary>How deep the index pages; further results are not reachable.</summary>
    public const int MaxWindow = 10000;

    public static readonly Error QueryInvalid = Error.Validation("Search.QueryInvalid", $"Enter between 1 and {MaxQueryLength} characters to search for.");

    public static readonly Error Unavailable = Error.Unavailable("Search.Unavailable", "Search is unavailable at the moment. Try again shortly.");
}

/// <summary>Published movies and series matching the text, best match first.</summary>
public sealed record SearchTitlesQuery(string? Text, string Language, TitleKind? Kind, Guid? GenreId, int PageNumber, int PageSize) : IQuery<Page<SearchHit>>;

public sealed record SearchHit(
    Guid Id,
    TitleKind Kind,
    string Title,
    string Language,
    int ReleaseYear,
    AgeRating AgeRating,
    Uri? PosterUrl,
    IReadOnlyList<GenreReference> Genres);

internal sealed class SearchTitlesValidator : IValidator<SearchTitlesQuery>
{
    public IEnumerable<ValidationFailure> Validate(SearchTitlesQuery instance)
    {
        foreach (var failure in LanguageValidation.Validate(instance.Language))
        {
            yield return failure;
        }

        if (string.IsNullOrWhiteSpace(instance.Text) || instance.Text.Length > SearchErrors.MaxQueryLength)
        {
            yield return new ValidationFailure("q", SearchErrors.QueryInvalid.Code, SearchErrors.QueryInvalid.Message);
        }

        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize)
            || PageLimits.Offset(instance.PageNumber, instance.PageSize) + instance.PageSize > SearchErrors.MaxWindow)
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class SearchTitlesHandler(ISearchIndex index, IImageStorage storage) : IQueryHandler<SearchTitlesQuery, Page<SearchHit>>
{
    public async Task<Result<Page<SearchHit>>> Handle(SearchTitlesQuery query, CancellationToken cancellationToken)
    {
        ContentKind? kind = query.Kind switch
        {
            TitleKind.Movie => ContentKind.Movie,
            TitleKind.Series => ContentKind.Series,
            _ => null,
        };

        try
        {
            var found = await index
                .SearchAsync(new SearchQuery(query.Text!.Trim(), query.Language, kind, query.GenreId, PageLimits.Offset(query.PageNumber, query.PageSize), query.PageSize), cancellationToken)
                .ConfigureAwait(false);

            var items = found.Items.Select(document => SearchHits.ToHit(document, query.Language, storage)).ToList();
            return new Page<SearchHit>(items, query.PageNumber, query.PageSize, (int)Math.Min(found.Total, int.MaxValue));
        }
        catch (SearchUnavailableException)
        {
            return SearchErrors.Unavailable;
        }
    }
}

internal static class SearchHits
{
    public static SearchHit ToHit(SearchDocument document, string language, IImageStorage storage)
    {
        var shown = document.Title.ContainsKey(language) ? language : Languages.Default;
        return new SearchHit(
            document.Id,
            document.Kind == ContentKind.Movie ? TitleKind.Movie : TitleKind.Series,
            document.Title[shown],
            shown,
            document.ReleaseYear,
            document.AgeRating,
            document.PosterImage is { } poster ? storage.PublicAddress(poster) : null,
            [.. document.GenreList.Select(genre => new GenreReference(genre.Id, genre.Names.GetValueOrDefault(language) ?? genre.Names[Languages.Default]))]);
    }
}
