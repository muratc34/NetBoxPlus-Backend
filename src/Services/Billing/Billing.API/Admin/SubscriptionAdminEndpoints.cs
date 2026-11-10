using Billing.Application.Subscriptions.GetSubscription;
using Billing.Application.Subscriptions.GetSubscriptions;
using Billing.Domain.Subscriptions;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Billing.API.Admin;

/// <summary>Subscriptions, read only: they change at the payment provider, never here.</summary>
internal static class SubscriptionAdminEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/subscriptions").WithTags("Subscriptions").RequirePermission(BillingPermissions.SubscriptionsRead);

        group.MapGet("/", GetSubscriptionsAsync)
            .WithName("GetSubscriptions")
            .WithSummary("Lists subscriptions, newest first.")
            .Produces<Page<SubscriptionSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetSubscriptionAsync)
            .WithName("GetSubscription")
            .WithSummary("Returns a subscription with its invoices.")
            .Produces<SubscriptionDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetSubscriptionsAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] SubscriptionStatus? status = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetSubscriptionsQuery(status, userId, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetSubscriptionAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetSubscriptionQuery(id), cancellationToken)).ToHttpResult();
}
