using Billing.Application.Abstractions;
using Billing.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Billing.Application.Plans.GetPlan;

public sealed record GetPlanQuery(Guid PlanId) : IQuery<PlanDetails>;

/// <param name="Name">In the default language.</param>
/// <param name="Version">To send back with a change.</param>
public sealed record PlanDetails(
    Guid Id,
    string Name,
    IReadOnlyDictionary<string, PlanTextFields> Translations,
    long AmountMinor,
    string Currency,
    VideoQuality Quality,
    int MaxScreens,
    string ProviderPriceId,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

internal sealed class GetPlanHandler(IBillingReadModel readModel) : IQueryHandler<GetPlanQuery, PlanDetails>
{
    public async Task<Result<PlanDetails>> Handle(GetPlanQuery query, CancellationToken cancellationToken)
    {
        var plan = await readModel.Plans.FirstOrDefaultAsync(item => item.Id == query.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return PlanErrors.NotFound;
        }

        return new PlanDetails(
            plan.Id,
            plan.TextFor(Languages.Default).Name,
            plan.Translations.ToDictionary(item => item.Language, item => new PlanTextFields(item.Name, item.Description)),
            plan.AmountMinor,
            plan.Currency,
            plan.Quality,
            plan.MaxScreens,
            plan.ProviderPriceId,
            plan.IsActive,
            plan.Version,
            plan.CreatedAt,
            plan.ModifiedAt);
    }
}
