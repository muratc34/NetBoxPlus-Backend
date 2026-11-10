using Billing.Application.Abstractions;
using Billing.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Plans.GetPlans;

/// <summary>Every plan for the admin panel, cheapest first; inactive ones too unless filtered.</summary>
public sealed record GetPlansQuery(bool? Active, int PageNumber, int PageSize) : IQuery<Page<PlanSummary>>;

public sealed record PlanSummary(Guid Id, string Name, long AmountMinor, string Currency, VideoQuality Quality, int MaxScreens, bool IsActive);

internal sealed class GetPlansValidator : IValidator<GetPlansQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetPlansQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetPlansHandler(IBillingReadModel readModel) : IQueryHandler<GetPlansQuery, Page<PlanSummary>>
{
    public async Task<Result<Page<PlanSummary>>> Handle(GetPlansQuery query, CancellationToken cancellationToken)
    {
        var plans = readModel.Plans;
        if (query.Active is { } active)
        {
            plans = plans.Where(plan => plan.IsActive == active);
        }

        var totalCount = await plans.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await plans
            .OrderBy(plan => plan.AmountMinor)
            .ThenBy(plan => plan.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = page
            .Select(plan => new PlanSummary(plan.Id, plan.TextFor(Languages.Default).Name, plan.AmountMinor, plan.Currency, plan.Quality, plan.MaxScreens, plan.IsActive))
            .ToList();

        return new Page<PlanSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
