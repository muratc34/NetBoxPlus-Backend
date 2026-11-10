using Billing.Application.Plans.ChangePlanSale;
using Billing.Application.Plans.CreatePlan;
using Billing.Application.Plans.GetPlan;
using Billing.Application.Plans.GetPlans;
using Billing.Application.Plans.UpdatePlan;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Billing.API.Admin;

/// <summary>Plans, for the admin panel. Plans are never deleted, only taken off sale. Every change is written to the audit log.</summary>
internal static class PlanAdminEndpoints
{
    public static IEndpointRouteBuilder MapPlanAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/plans").WithTags("Plans");

        group.MapGet("/", GetPlansAsync)
            .WithName("GetPlans")
            .WithSummary("Lists plans, cheapest first.")
            .RequirePermission(BillingPermissions.PlansRead)
            .Produces<Page<PlanSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetPlanAsync)
            .WithName("GetPlan")
            .WithSummary("Returns a plan with every translation and its version.")
            .RequirePermission(BillingPermissions.PlansRead)
            .Produces<PlanDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreatePlan")
            .WithSummary("Adds a plan, not yet on sale.")
            .RequirePermission(BillingPermissions.PlansWrite)
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdatePlan")
            .WithSummary("Replaces a plan. Fails if it changed since the given version.")
            .RequirePermission(BillingPermissions.PlansWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivatePlan")
            .WithSummary("Puts a plan on sale.")
            .RequirePermission(BillingPermissions.PlansWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivatePlan")
            .WithSummary("Takes a plan off sale; existing subscriptions are kept.")
            .RequirePermission(BillingPermissions.PlansWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetPlansAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] bool? active = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetPlansQuery(active, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetPlanAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetPlanQuery(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync([FromBody] PlanRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new CreatePlanCommand(request.Fields), cancellationToken);

        return result.ToHttpResult(id => TypedResults.Created($"/admin/plans/{id}", new CreatedResponse(id)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] PlanRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdatePlanCommand(id, request.Fields, request.Version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> ActivateAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new ActivatePlanCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeactivateAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new DeactivatePlanCommand(id), cancellationToken)).ToHttpResult();
}
