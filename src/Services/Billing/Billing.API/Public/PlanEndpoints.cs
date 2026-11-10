using Billing.Application.Plans.GetPlansOnSale;
using Billing.Domain.Plans;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Billing.API.Public;

/// <summary>What anyone can see before signing up: the plans on sale.</summary>
internal static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/plans", GetPlansOnSaleAsync)
            .WithTags("Plans")
            .WithName("GetPlansOnSale")
            .WithSummary("Lists the plans on sale, cheapest first.")
            .Produces<IReadOnlyList<PlanOffer>>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetPlansOnSaleAsync([FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetPlansOnSaleQuery(lang ?? Languages.Default), cancellationToken)).ToHttpResult();
}
