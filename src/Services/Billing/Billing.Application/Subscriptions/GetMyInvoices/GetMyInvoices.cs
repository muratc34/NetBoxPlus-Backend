using Billing.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Subscriptions.GetMyInvoices;

/// <summary>The caller's invoices, newest first.</summary>
public sealed record GetMyInvoicesQuery(Guid UserId, int PageNumber, int PageSize) : IQuery<Page<InvoiceSummary>>;

internal sealed class GetMyInvoicesValidator : IValidator<GetMyInvoicesQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetMyInvoicesQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetMyInvoicesHandler(IBillingReadModel readModel) : IQueryHandler<GetMyInvoicesQuery, Page<InvoiceSummary>>
{
    public async Task<Result<Page<InvoiceSummary>>> Handle(GetMyInvoicesQuery query, CancellationToken cancellationToken)
    {
        var invoices = readModel.Invoices.Where(invoice => invoice.UserId == query.UserId);

        var totalCount = await invoices.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await invoices
            .OrderByDescending(invoice => invoice.IssuedAt)
            .ThenByDescending(invoice => invoice.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Page<InvoiceSummary>([.. page.Select(invoice => invoice.ToSummary())], query.PageNumber, query.PageSize, totalCount);
    }
}
