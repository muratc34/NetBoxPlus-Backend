using Billing.Application.Subscriptions;
using Billing.Application.Subscriptions.GetMyInvoices;
using Billing.Application.Subscriptions.GetMySubscription;
using Billing.Domain.Plans;
using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;

namespace Billing.API.Account;

/// <summary>A signed-in customer's own subscription and invoices; nobody else's can be reached here.</summary>
internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(string.Empty).WithTags("Account").RequireAuthorization();

        group.MapGet("/subscription", GetSubscriptionAsync)
            .WithName("GetMySubscription")
            .WithSummary("Returns the caller's live subscription, or the latest that ended.")
            .Produces<MySubscription>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/invoices", GetInvoicesAsync)
            .WithName("GetMyInvoices")
            .WithSummary("Lists the caller's invoices, newest first.")
            .Produces<Page<InvoiceSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> GetSubscriptionAsync(
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? lang = null)
        => (await dispatcher.Query(new GetMySubscriptionQuery(currentUser.Id!.Value, lang ?? Languages.Default), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetInvoicesAsync(
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetMyInvoicesQuery(currentUser.Id!.Value, page, pageSize), cancellationToken)).ToHttpResult();
}
