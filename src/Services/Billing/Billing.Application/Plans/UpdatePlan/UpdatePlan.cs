using Billing.Domain.Plans;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Billing.Application.Plans.UpdatePlan;

/// <summary>Replaces a plan. A new price applies to new sales only: existing subscriptions keep the price they were sold at.</summary>
public sealed record UpdatePlanCommand(Guid PlanId, PlanFields Fields, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => PlanId;
}

internal sealed class UpdatePlanHandler(IPlanRepository plans) : ICommandHandler<UpdatePlanCommand>
{
    public async Task<Result> Handle(UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(command.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return PlanErrors.NotFound;
        }

        var current = plan.CheckVersion(command.Version);
        if (current.IsFailure)
        {
            return current;
        }

        var changed = plan.Change(command.Fields.Texts, command.Fields.Terms);
        if (changed.IsFailure)
        {
            return changed;
        }

        return await plans.IsProviderPriceTakenAsync(plan, cancellationToken).ConfigureAwait(false)
            ? PlanErrors.ProviderPriceTaken
            : Result.Success();
    }
}
