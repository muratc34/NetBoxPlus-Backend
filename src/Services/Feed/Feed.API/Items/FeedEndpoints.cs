using Feed.Application.Items.GetFeed;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Feed.API.Items;

internal static class FeedEndpoints
{
    public static IEndpointRouteBuilder MapFeedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var items = endpoints.MapGroup("/items").WithTags("Feed");

        // Anonymous until the Auth service exists (phase 1). Items are added only from events.
        items.MapGet("/", GetAsync)
            .WithName("GetFeed")
            .WithSummary("Returns the most recently added items, newest first.")
            .Produces<IReadOnlyList<FeedItemResponse>>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] int limit = GetFeedQuery.DefaultLimit)
        => (await dispatcher.Query(new GetFeedQuery(limit), cancellationToken)).ToHttpResult();
}
