using Catalog.Application.Shows.AddEpisode;
using Catalog.Application.Shows.AddSeason;
using Catalog.Application.Shows.CreateSeries;
using Catalog.Application.Shows.DeleteSeries;
using Catalog.Application.Shows.GetSeries;
using Catalog.Application.Shows.GetSeriesList;
using Catalog.Application.Shows.PublishEpisode;
using Catalog.Application.Shows.PublishSeries;
using Catalog.Application.Shows.RemoveEpisode;
using Catalog.Application.Shows.RemoveSeason;
using Catalog.Application.Shows.UnpublishEpisode;
using Catalog.Application.Shows.UnpublishSeries;
using Catalog.Application.Shows.UpdateEpisode;
using Catalog.Application.Shows.UpdateSeason;
using Catalog.Application.Shows.UpdateSeries;
using Catalog.Domain.Content;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Catalog.API.Admin;

/// <summary>Series with their seasons and episodes, for the admin panel. Every change is written to the audit log.</summary>
internal static class SeriesAdminEndpoints
{
    public static IEndpointRouteBuilder MapSeriesAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/series").WithTags("Series");

        group.MapGet("/", GetListAsync)
            .WithName("GetAdminSeriesList")
            .WithSummary("Lists series, drafts included, newest first.")
            .RequirePermission(CatalogPermissions.SeriesRead)
            .Produces<Page<SeriesSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAdminSeries")
            .WithSummary("Returns a series with its seasons, episodes and version.")
            .RequirePermission(CatalogPermissions.SeriesRead)
            .Produces<SeriesDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateSeries")
            .WithSummary("Creates a draft series.")
            .RequirePermission(CatalogPermissions.SeriesWrite)
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSeries")
            .WithSummary("Replaces a series. Fails if it changed since the given version.")
            .RequirePermission(CatalogPermissions.SeriesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/publish", PublishAsync)
            .WithName("PublishSeries")
            .WithSummary("Makes a series visible to customers.")
            .RequirePermission(CatalogPermissions.SeriesPublish)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/unpublish", UnpublishAsync)
            .WithName("UnpublishSeries")
            .WithSummary("Hides a series from customers.")
            .RequirePermission(CatalogPermissions.SeriesPublish)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSeries")
            .WithSummary("Deletes an unpublished series.")
            .RequirePermission(CatalogPermissions.SeriesDelete)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/seasons", AddSeasonAsync)
            .WithName("AddSeason")
            .WithSummary("Adds a season.")
            .RequirePermission(CatalogPermissions.SeriesWrite)
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}/seasons/{seasonId:guid}", UpdateSeasonAsync)
            .WithName("UpdateSeason")
            .WithSummary("Changes a season.")
            .RequirePermission(CatalogPermissions.SeriesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/seasons/{seasonId:guid}", RemoveSeasonAsync)
            .WithName("RemoveSeason")
            .WithSummary("Removes a season without published episodes.")
            .RequirePermission(CatalogPermissions.SeriesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/seasons/{seasonId:guid}/episodes", AddEpisodeAsync)
            .WithName("AddEpisode")
            .WithSummary("Adds an episode, hidden until published.")
            .RequirePermission(CatalogPermissions.SeriesWrite)
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}/episodes/{episodeId:guid}", UpdateEpisodeAsync)
            .WithName("UpdateEpisode")
            .WithSummary("Changes an episode.")
            .RequirePermission(CatalogPermissions.SeriesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/episodes/{episodeId:guid}", RemoveEpisodeAsync)
            .WithName("RemoveEpisode")
            .WithSummary("Removes an unpublished episode.")
            .RequirePermission(CatalogPermissions.SeriesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/episodes/{episodeId:guid}/publish", PublishEpisodeAsync)
            .WithName("PublishEpisode")
            .WithSummary("Makes an episode visible.")
            .RequirePermission(CatalogPermissions.SeriesPublish)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/episodes/{episodeId:guid}/unpublish", UnpublishEpisodeAsync)
            .WithName("UnpublishEpisode")
            .WithSummary("Hides an episode.")
            .RequirePermission(CatalogPermissions.SeriesPublish)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] PublicationStatus? status = null,
        [FromQuery] Guid? genreId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetSeriesListQuery(search, status, genreId, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetSeriesQuery(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync([FromBody] SeriesRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new CreateSeriesCommand(request.Fields), cancellationToken);

        return result.ToHttpResult(id => TypedResults.Created($"/admin/series/{id}", new CreatedResponse(id)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] SeriesRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdateSeriesCommand(id, request.Fields, request.Version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> PublishAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new PublishSeriesCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> UnpublishAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UnpublishSeriesCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new DeleteSeriesCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> AddSeasonAsync(Guid id, [FromBody] SeasonRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new AddSeasonCommand(id, request.Number, request.Title, request.Version), cancellationToken);

        return result.ToHttpResult(created => TypedResults.Created($"/admin/series/{id}", new CreatedResponse(created)));
    }

    private static async Task<IResult> UpdateSeasonAsync(Guid id, Guid seasonId, [FromBody] SeasonRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdateSeasonCommand(id, seasonId, request.Number, request.Title, request.Version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveSeasonAsync(Guid id, Guid seasonId, [FromQuery] int version, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveSeasonCommand(id, seasonId, version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> AddEpisodeAsync(Guid id, Guid seasonId, [FromBody] EpisodeRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new AddEpisodeCommand(id, seasonId, request.Number, request.Title, request.Synopsis, request.RuntimeMinutes, request.Version), cancellationToken);

        return result.ToHttpResult(created => TypedResults.Created($"/admin/series/{id}", new CreatedResponse(created)));
    }

    private static async Task<IResult> UpdateEpisodeAsync(Guid id, Guid episodeId, [FromBody] EpisodeRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdateEpisodeCommand(id, episodeId, request.Number, request.Title, request.Synopsis, request.RuntimeMinutes, request.Version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveEpisodeAsync(Guid id, Guid episodeId, [FromQuery] int version, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveEpisodeCommand(id, episodeId, version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> PublishEpisodeAsync(Guid id, Guid episodeId, [FromQuery] int version, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new PublishEpisodeCommand(id, episodeId, version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> UnpublishEpisodeAsync(Guid id, Guid episodeId, [FromQuery] int version, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UnpublishEpisodeCommand(id, episodeId, version), cancellationToken)).ToHttpResult();
}
