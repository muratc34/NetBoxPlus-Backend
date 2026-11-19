using Catalog.Application.Abstractions;
using Catalog.Application.Browse.Search;
using Catalog.Application.Content;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Browse.Suggest;

/// <summary>Titles matching what has been typed so far.</summary>
public sealed record SuggestTitlesQuery(string? Text, string Language) : IQuery<IReadOnlyList<SearchHit>>;

internal sealed class SuggestTitlesValidator : IValidator<SuggestTitlesQuery>
{
    public IEnumerable<ValidationFailure> Validate(SuggestTitlesQuery instance)
    {
        foreach (var failure in LanguageValidation.Validate(instance.Language))
        {
            yield return failure;
        }

        if (string.IsNullOrWhiteSpace(instance.Text) || instance.Text.Length > SearchErrors.MaxQueryLength)
        {
            yield return new ValidationFailure("q", SearchErrors.QueryInvalid.Code, SearchErrors.QueryInvalid.Message);
        }
    }
}

internal sealed class SuggestTitlesHandler(ISearchIndex index, IImageStorage storage) : IQueryHandler<SuggestTitlesQuery, IReadOnlyList<SearchHit>>
{
    private const int MaxSuggestions = 10;

    public async Task<Result<IReadOnlyList<SearchHit>>> Handle(SuggestTitlesQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var found = await index.SuggestAsync(query.Text!.Trim(), query.Language, MaxSuggestions, cancellationToken).ConfigureAwait(false);
            return found.Select(document => SearchHits.ToHit(document, query.Language, storage)).ToList();
        }
        catch (SearchUnavailableException)
        {
            return SearchErrors.Unavailable;
        }
    }
}
