using Billing.Application.Checkout.OpenPortal;
using Billing.Application.Checkout.StartCheckout;
using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Billing.API.Checkout;

public sealed record StartCheckoutRequest(Guid PlanId) : IIdempotentBody;

/// <summary>A signed-in customer's way to the payment provider's pages. Both need an Idempotency-Key.</summary>
internal static class CheckoutEndpoints
{
    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty).WithTags("Checkout").RequireAuthorization();

        group.MapPost("/checkout-sessions", StartCheckoutAsync)
            .WithName("StartCheckout")
            .WithSummary("Opens the payment provider's checkout page for a plan on sale.")
            .AddEndpointFilter<IdempotencyFilter>()
            .Produces<CheckoutSession>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/portal-sessions", OpenPortalAsync)
            .WithName("OpenBillingPortal")
            .WithSummary("Opens the payment provider's page for managing card, plan and cancellation.")
            .AddEndpointFilter<IdempotencyFilter>()
            .Produces<PortalSession>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    // The key is read by IdempotencyFilter; it is a parameter here so that it is part of the API description.
    private static async Task<IResult> StartCheckoutAsync(
        [FromBody] StartCheckoutRequest request,
        [FromHeader(Name = IdempotencyFilter.HeaderName)] string? idempotencyKey,
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new StartCheckoutCommand(currentUser.Id!.Value, request.PlanId, idempotencyKey!), cancellationToken)).ToHttpResult();

    private static async Task<IResult> OpenPortalAsync(
        [FromHeader(Name = IdempotencyFilter.HeaderName)] string? idempotencyKey,
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new OpenPortalCommand(currentUser.Id!.Value), cancellationToken)).ToHttpResult();
}
