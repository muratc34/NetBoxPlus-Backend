using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;
using Streaming.Application.Lists;

namespace Streaming.API.Lists;

/// <summary>A profile's "my list": titles saved to watch later.</summary>
internal static class MyListEndpoints
{
    public static IEndpointRouteBuilder MapMyListEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var list = endpoints.MapGroup("/profiles/{profileId:guid}/my-list").WithTags("MyList").RequireAuthorization();

        list.MapGet(string.Empty, GetAsync)
            .WithName("GetMyList")
            .WithSummary("The titles on the profile's list that it can watch now, most recently added first.")
            .Produces<IReadOnlyList<MyListItem>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        list.MapPut("/{titleId:guid}", AddAsync)
            .WithName("AddToMyList")
            .WithSummary("Puts a title the profile can watch on its list; one already there stays. At most 200 titles.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        list.MapDelete("/{titleId:guid}", RemoveAsync)
            .WithName("RemoveFromMyList")
            .WithSummary("Takes a title off the profile's list; one that is not there changes nothing.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid profileId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetMyListQuery(currentUser.Id!.Value, profileId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> AddAsync(Guid profileId, Guid titleId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Send(new AddToMyListCommand(currentUser.Id!.Value, profileId, titleId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveAsync(Guid profileId, Guid titleId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveFromMyListCommand(currentUser.Id!.Value, profileId, titleId), cancellationToken)).ToHttpResult();
}
