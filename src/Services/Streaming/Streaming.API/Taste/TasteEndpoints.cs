using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Streaming.Application.History;
using Streaming.Application.Ratings;
using Streaming.Domain.Ratings;

namespace Streaming.API.Taste;

/// <param name="Rating">NotForMe, Like or Love.</param>
public sealed record RateTitleRequest(RatingLevel? Rating);

/// <summary>What shapes a profile's recommendations: its ratings, its watching history, and the two together.</summary>
internal static class TasteEndpoints
{
    public static IEndpointRouteBuilder MapTasteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var profiles = endpoints.MapGroup("/profiles/{profileId:guid}").WithTags("Taste").RequireAuthorization();

        profiles.MapGet("/ratings/{titleId:guid}", GetRatingAsync)
            .WithName("GetTitleRating")
            .WithSummary("How the profile rated a title; the rating is null when it has not.")
            .Produces<TitleRatingView>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        profiles.MapPut("/ratings/{titleId:guid}", RateAsync)
            .WithName("RateTitle")
            .WithSummary("Rates a title the profile can watch, or changes its rating.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        profiles.MapDelete("/ratings/{titleId:guid}", RemoveRatingAsync)
            .WithName("RemoveTitleRating")
            .WithSummary("Takes back the profile's rating of a title; one it has not rated changes nothing.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        profiles.MapGet("/history", GetHistoryAsync)
            .WithName("GetWatchHistory")
            .WithSummary("The titles the profile has watched and can watch now, the most recently watched first.")
            .Produces<Page<HistoryItem>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        profiles.MapDelete("/history/{titleId:guid}", RemoveFromHistoryAsync)
            .WithName("RemoveFromWatchHistory")
            .WithSummary("Forgets that the profile watched a title: it leaves the history, continue watching and the profile's taste.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        profiles.MapGet("/taste", GetTasteAsync)
            .WithName("GetTaste")
            .WithSummary("Title ids the profile is drawn to and away from, strongest first, for the catalog's recommendations.")
            .Produces<Domain.Ratings.Taste>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> GetRatingAsync(Guid profileId, Guid titleId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetRatingQuery(currentUser.Id!.Value, profileId, titleId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RateAsync(
        Guid profileId,
        Guid titleId,
        RateTitleRequest request,
        [FromServices] IDispatcher dispatcher,
        [FromServices] ICurrentUser currentUser,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new RateTitleCommand(currentUser.Id!.Value, profileId, titleId, request.Rating), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveRatingAsync(Guid profileId, Guid titleId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveRatingCommand(currentUser.Id!.Value, profileId, titleId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetHistoryAsync(
        Guid profileId,
        [FromServices] IDispatcher dispatcher,
        [FromServices] ICurrentUser currentUser,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetHistoryQuery(currentUser.Id!.Value, profileId, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveFromHistoryAsync(Guid profileId, Guid titleId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveFromHistoryCommand(currentUser.Id!.Value, profileId, titleId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetTasteAsync(Guid profileId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetTasteQuery(currentUser.Id!.Value, profileId), cancellationToken)).ToHttpResult();
}
