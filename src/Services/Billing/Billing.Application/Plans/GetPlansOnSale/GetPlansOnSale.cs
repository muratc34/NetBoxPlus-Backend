using Billing.Application.Abstractions;
using Billing.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Plans.GetPlansOnSale;

/// <summary>The plans a customer can buy, cheapest first; there are few, so the list is not paged.</summary>
public sealed record GetPlansOnSaleQuery(string Language) : IQuery<IReadOnlyList<PlanOffer>>;

/// <param name="Language">The language the name and description are actually in.</param>
public sealed record PlanOffer(Guid Id, string Name, string Description, string Language, long AmountMinor, string Currency, VideoQuality Quality, int MaxScreens);

internal sealed class GetPlansOnSaleValidator : IValidator<GetPlansOnSaleQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetPlansOnSaleQuery instance) => LanguageValidation.Validate(instance.Language);
}

internal sealed class GetPlansOnSaleHandler(IBillingReadModel readModel) : IQueryHandler<GetPlansOnSaleQuery, IReadOnlyList<PlanOffer>>
{
    public async Task<Result<IReadOnlyList<PlanOffer>>> Handle(GetPlansOnSaleQuery query, CancellationToken cancellationToken)
    {
        var plans = await readModel.Plans
            .Where(plan => plan.IsActive)
            .OrderBy(plan => plan.AmountMinor)
            .ThenBy(plan => plan.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return plans
            .Select(plan =>
            {
                var text = plan.TextFor(query.Language);
                return new PlanOffer(plan.Id, text.Name, text.Description, text.Language, plan.AmountMinor, plan.Currency, plan.Quality, plan.MaxScreens);
            })
            .ToList();
    }
}
