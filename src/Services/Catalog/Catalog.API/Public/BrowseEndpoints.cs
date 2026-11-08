using Catalog.Application.Browse.GetPublicPerson;
using Catalog.Application.Browse.GetPublishedGenres;
using Catalog.Application.Browse.GetPublishedMovie;
using Catalog.Application.Browse.GetPublishedSeries;
using Catalog.Application.Browse.GetTitles;
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
            .WithSummary("Lists published movies and series, most recently published first.")
            .Produces<Page<TitleSummary>>()
            .ProducesValidationProblem();

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
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetTitlesQuery(lang ?? Languages.Default, kind, genreId, personId, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetMovieAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPublishedMovieQuery(id, lang ?? Languages.Default), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetSeriesAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPublishedSeriesQuery(id, lang ?? Languages.Default), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetGenresAsync([FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPublishedGenresQuery(lang ?? Languages.Default), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetPersonAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPublicPersonQuery(id, lang ?? Languages.Default), cancellationToken)).ToHttpResult();
}
