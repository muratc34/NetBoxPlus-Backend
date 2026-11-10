namespace Billing.Domain.Plans;

public interface IPlanRepository
{
    Task<Plan?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another plan is already sold at this plan's provider price.</summary>
    Task<bool> IsProviderPriceTakenAsync(Plan plan, CancellationToken cancellationToken);

    void Add(Plan plan);
}
