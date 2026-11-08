using Catalog.Application.Genres.CreateGenre;
using Catalog.Application.Genres.DeleteGenre;
using Catalog.Application.Genres.GetGenres;
using Catalog.Application.Genres.UpdateGenre;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Catalog.API.Admin;

/// <summary>Genres, for the admin panel. Every change is written to the audit log.</summary>
internal static class GenreAdminEndpoints
{
    public static IEndpointRouteBuilder MapGenreAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/genres").WithTags("Genres");

        group.MapGet("/", GetGenresAsync)
            .WithName("GetGenres")
            .WithSummary("Lists genres with how much they are used.")
            .RequirePermission(CatalogPermissions.GenresRead)
            .Produces<IReadOnlyList<GenreResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .WithName("CreateGenre")
            .WithSummary("Adds a genre.")
            .RequirePermission(CatalogPermissions.GenresWrite)
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateGenre")
            .WithSummary("Renames a genre.")
            .RequirePermission(CatalogPermissions.GenresWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteGenre")
            .WithSummary("Deletes a genre nothing uses.")
            .RequirePermission(CatalogPermissions.GenresWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetGenresAsync([FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetGenresQuery(), cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync([FromBody] GenreRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new CreateGenreCommand(request.Name), cancellationToken);

        return result.ToHttpResult(id => TypedResults.Created($"/admin/genres", new CreatedResponse(id)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] GenreRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdateGenreCommand(id, request.Name), cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new DeleteGenreCommand(id), cancellationToken)).ToHttpResult();
}
