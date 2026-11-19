using Catalog.Application.Movies.CreateMovie;
using Catalog.Application.Movies.DeleteMovie;
using Catalog.Application.Movies.GetMovie;
using Catalog.Application.Movies.GetMovies;
using Catalog.Application.Movies.PublishMovie;
using Catalog.Application.Movies.UnpublishMovie;
using Catalog.Application.Movies.UpdateMovie;
using Catalog.Domain.Content;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Catalog.API.Admin;

/// <summary>Movies, for the admin panel. Every change is written to the audit log.</summary>
internal static class MovieAdminEndpoints
{
    public static IEndpointRouteBuilder MapMovieAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/movies").WithTags("Movies");

        group.MapGet("/", GetMoviesAsync)
            .WithName("GetAdminMovies")
            .WithSummary("Lists movies, drafts included, newest first.")
            .RequirePermission(CatalogPermissions.MoviesRead)
            .Produces<Page<MovieSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetMovieAsync)
            .WithName("GetAdminMovie")
            .WithSummary("Returns a movie with its version.")
            .RequirePermission(CatalogPermissions.MoviesRead)
            .Produces<MovieDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateMovie")
            .WithSummary("Creates a draft movie.")
            .RequirePermission(CatalogPermissions.MoviesWrite)
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateMovie")
            .WithSummary("Replaces a movie. Fails if it changed since the given version.")
            .RequirePermission(CatalogPermissions.MoviesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/publish", PublishAsync)
            .WithName("PublishMovie")
            .WithSummary("Makes a movie visible to customers.")
            .RequirePermission(CatalogPermissions.MoviesPublish)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/unpublish", UnpublishAsync)
            .WithName("UnpublishMovie")
            .WithSummary("Hides a movie from customers.")
            .RequirePermission(CatalogPermissions.MoviesPublish)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteMovie")
            .WithSummary("Deletes an unpublished movie.")
            .RequirePermission(CatalogPermissions.MoviesDelete)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetMoviesAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] PublicationStatus? status = null,
        [FromQuery] Guid? genreId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetMoviesQuery(search, status, genreId, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetMovieAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetMovieQuery(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync([FromBody] MovieRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new CreateMovieCommand(request.Fields, request.RuntimeMinutes), cancellationToken);

        return result.ToHttpResult(id => TypedResults.Created($"/admin/movies/{id}", new CreatedResponse(id)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] MovieRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdateMovieCommand(id, request.Fields, request.RuntimeMinutes, request.Version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> PublishAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new PublishMovieCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> UnpublishAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UnpublishMovieCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new DeleteMovieCommand(id), cancellationToken)).ToHttpResult();
}
