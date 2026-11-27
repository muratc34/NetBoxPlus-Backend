using Catalog.Application.Abstractions;
using Catalog.Application.Browse.Search;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Catalog.Application.Browse.Recommend;

public static class RecommendationErrors
{
    public const int MaxLike = 20;

    public const int MaxUnlike = 20;

    public const int MaxExclude = 50;

    public static readonly Error LikeRequired = Error.Validation("Recommendations.LikeRequired", "Give at least one title to recommend from.");

    public static readonly Error TooManyIds = Error.Validation(
        "Recommendations.TooManyIds",
        $"Give at most {MaxLike} titles to like, {MaxUnlike} to unlike and {MaxExclude} to leave out.");
}

/// <summary>Published titles like this one: by genres, people, synopsis and title, the closest first.</summary>
/// <param name="MaxAgeRating">Only titles a viewer of this age may see; all when null.</param>
public sealed record GetSimilarTitlesQuery(Guid TitleId, string Language, int PageNumber, int PageSize, AgeRating? MaxAgeRating = null) : IQuery<Page<SearchHit>>;

/// <summary>
/// Published titles like the <paramref name="Like"/> ones, steering away from what the <paramref name="Unlike"/>
/// ones have in common with them; none of the given titles is returned. The ids come from a profile's taste,
/// which the catalog never sees: the caller passes them, strongest first.
/// </summary>
public sealed record GetRecommendedTitlesQuery(
    IReadOnlyList<Guid> Like,
    IReadOnlyList<Guid> Unlike,
    IReadOnlyList<Guid> Exclude,
    string Language,
    int PageNumber,
    int PageSize,
    AgeRating? MaxAgeRating = null) : IQuery<Page<SearchHit>>;

/// <summary>The <c>kind</c> tag of the recommendations metric.</summary>
public static class RecommendationKinds
{
    public const string Similar = "similar";

    public const string Recommended = "recommended";
}

internal static class RecommendationValidation
{
    public static IEnumerable<ValidationFailure> Common(string language, int pageNumber, int pageSize)
    {
        foreach (var failure in LanguageValidation.Validate(language))
        {
            yield return failure;
        }

        if (!PageLimits.IsValid(pageNumber, pageSize) || PageLimits.Offset(pageNumber, pageSize) + pageSize > SearchErrors.MaxWindow)
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetSimilarTitlesValidator : IValidator<GetSimilarTitlesQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetSimilarTitlesQuery instance)
        => RecommendationValidation.Common(instance.Language, instance.PageNumber, instance.PageSize);
}

internal sealed class GetRecommendedTitlesValidator : IValidator<GetRecommendedTitlesQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetRecommendedTitlesQuery instance)
    {
        foreach (var failure in RecommendationValidation.Common(instance.Language, instance.PageNumber, instance.PageSize))
        {
            yield return failure;
        }

        if (instance.Like.Count == 0)
        {
            yield return new ValidationFailure("like", RecommendationErrors.LikeRequired.Code, RecommendationErrors.LikeRequired.Message);
        }

        if (instance.Like.Count > RecommendationErrors.MaxLike || instance.Unlike.Count > RecommendationErrors.MaxUnlike || instance.Exclude.Count > RecommendationErrors.MaxExclude)
        {
            yield return new ValidationFailure("like", RecommendationErrors.TooManyIds.Code, RecommendationErrors.TooManyIds.Message);
        }
    }
}

/// <summary>A title that is not published, or unknown, has nothing like it: the page is empty.</summary>
internal sealed class GetSimilarTitlesHandler(ISearchIndex index, IImageStorage storage, CatalogMetrics metrics) : IQueryHandler<GetSimilarTitlesQuery, Page<SearchHit>>
{
    public Task<Result<Page<SearchHit>>> Handle(GetSimilarTitlesQuery query, CancellationToken cancellationToken)
        => Recommendations.FindAsync(
            index,
            storage,
            metrics,
            RecommendationKinds.Similar,
            new RecommendQuery([query.TitleId], [], [], PageLimits.Offset(query.PageNumber, query.PageSize), query.PageSize, query.MaxAgeRating),
            query.Language,
            query.PageNumber,
            cancellationToken);
}

internal sealed class GetRecommendedTitlesHandler(ISearchIndex index, IImageStorage storage, CatalogMetrics metrics) : IQueryHandler<GetRecommendedTitlesQuery, Page<SearchHit>>
{
    public Task<Result<Page<SearchHit>>> Handle(GetRecommendedTitlesQuery query, CancellationToken cancellationToken)
        => Recommendations.FindAsync(
            index,
            storage,
            metrics,
            RecommendationKinds.Recommended,
            new RecommendQuery(query.Like, query.Unlike, query.Exclude, PageLimits.Offset(query.PageNumber, query.PageSize), query.PageSize, query.MaxAgeRating),
            query.Language,
            query.PageNumber,
            cancellationToken);
}

internal static class Recommendations
{
    public static async Task<Result<Page<SearchHit>>> FindAsync(
        ISearchIndex index,
        IImageStorage storage,
        CatalogMetrics metrics,
        string kind,
        RecommendQuery query,
        string language,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var found = await index.RecommendAsync(query, cancellationToken).ConfigureAwait(false);
            metrics.Recommended(kind, found.Total > 0 ? RecommendationResults.Found : RecommendationResults.Empty);
            var items = found.Items.Select(document => SearchHits.ToHit(document, language, storage)).ToList();
            return new Page<SearchHit>(items, pageNumber, query.Size, (int)Math.Min(found.Total, int.MaxValue));
        }
        catch (SearchUnavailableException)
        {
            metrics.Recommended(kind, RecommendationResults.Unavailable);
            return SearchErrors.Unavailable;
        }
    }
}
