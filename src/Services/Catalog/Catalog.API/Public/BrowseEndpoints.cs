using Catalog.Application.Browse.GetPublicPerson;
using Catalog.Application.Browse.GetPublishedGenres;
using Catalog.Application.Browse.GetPublishedMovie;
using Catalog.Application.Browse.GetPublishedSeries;
using Catalog.Application.Browse.GetTitles;
using Catalog.Application.Browse.Recommend;
using Catalog.Application.Browse.Search;
using Catalog.Application.Content;
using Catalog.Domain.Content;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;

namespace Catalog.API.Public;

/// <summary>
/// What customers can browse, without signing in: only published content. Subscription and age
/// checks belong to watching, not to looking at the catalog.
/// </summary>
internal static class BrowseEndpoints
{
    public static IEndpointRouteBuilder MapBrowseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var browse = endpoints.MapGroup(string.Empty).WithTags("Browse");

        browse.MapGet("/titles", GetTitlesAsync)
            .WithName("GetTitles")
            .WithSummary("Lists published movies and series, most recently published first, or watched most in the last week first with sort=Popular; with ids, only those titles (at most 100).")
            .Produces<Page<TitleSummary>>()
            .ProducesValidationProblem();

        browse.MapGet("/search", SearchAsync)
            .WithName("SearchTitles")
            .WithSummary("Searches published movies and series, best match first; the last word may be unfinished.")
            .Produces<Page<SearchHit>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        browse.MapGet("/titles/{id:guid}/similar", GetSimilarAsync)
            .WithName("GetSimilarTitles")
            .WithSummary("Published titles like this one, by genres, people, synopsis and title, the closest first; empty for a title that is not published.")
            .Produces<Page<SearchHit>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        browse.MapGet("/titles/recommended", GetRecommendedAsync)
            .WithName("GetRecommendedTitles")
            .WithSummary("Published titles like the liked ones (at most 20) and unlike the unliked ones (at most 20); none of the given or excluded (at most 50) titles is returned.")
            .Produces<Page<SearchHit>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        browse.MapGet("/movies/{id:guid}", GetMovieAsync)
            .WithName("GetPublishedMovie")
            .WithSummary("Returns a published movie.")
            .Produces<PublishedMovie>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        browse.MapGet("/series/{id:guid}", GetSeriesAsync)
            .WithName("GetPublishedSeries")
            .WithSummary("Returns a published series with its published episodes.")
            .Produces<PublishedSeries>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        browse.MapGet("/genres", GetGenresAsync)
            .WithName("GetPublishedGenres")
            .WithSummary("Lists the genres that have published content.")
            .Produces<IReadOnlyList<GenreReference>>()
            .ProducesValidationProblem();

        browse.MapGet("/people/{id:guid}", GetPersonAsync)
            .WithName("GetPublicPerson")
            .WithSummary("Returns someone credited on published content.")
            .Produces<PublicPerson>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetTitlesAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? lang = null,
        [FromQuery] TitleKind? kind = null,
        [FromQuery] Guid? genreId = null,
        [FromQuery] Guid? personId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize,
        [FromQuery] TitleSort sort = TitleSort.Newest,
        [FromQuery] AgeRating? maxAgeRating = null,
        [FromQuery] Guid[]? ids = null)
        => (await dispatcher.Query(new GetTitlesQuery(lang ?? Languages.Default, kind, genreId, personId, page, pageSize, sort, maxAgeRating, ids), cancellationToken)).ToHttpResult();

    private static async Task<IResult> SearchAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? q = null,
        [FromQuery] string? lang = null,
        [FromQuery] TitleKind? kind = null,
        [FromQuery] Guid? genreId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize,
        [FromQuery] AgeRating? maxAgeRating = null)
        => (await dispatcher.Query(new SearchTitlesQuery(q, lang ?? Languages.Default, kind, genreId, page, pageSize, maxAgeRating), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetSimilarAsync(
        Guid id,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? lang = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize,
        [FromQuery] AgeRating? maxAgeRating = null)
        => (await dispatcher.Query(new GetSimilarTitlesQuery(id, lang ?? Languages.Default, page, pageSize, maxAgeRating), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetRecommendedAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] Guid[]? like = null,
        [FromQuery] Guid[]? unlike = null,
        [FromQuery] Guid[]? exclude = null,
        [FromQuery] string? lang = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize,
        [FromQuery] AgeRating? maxAgeRating = null)
        => (await dispatcher.Query(new GetRecommendedTitlesQuery(like ?? [], unlike ?? [], exclude ?? [], lang ?? Languages.Default, page, pageSize, maxAgeRating), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetMovieAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPublishedMovieQuery(id, lang ?? Languages.Default), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetSeriesAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPublishedSeriesQuery(id, lang ?? Languages.Default), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetGenresAsync([FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPublishedGenresQuery(lang ?? Languages.Default), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetPersonAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPublicPersonQuery(id, lang ?? Languages.Default), cancellationToken)).ToHttpResult();
}
