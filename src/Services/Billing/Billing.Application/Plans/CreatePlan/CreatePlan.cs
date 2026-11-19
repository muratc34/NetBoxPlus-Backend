using Billing.Domain.Plans;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Billing.Application.Plans.CreatePlan;

/// <summary>Adds a plan; it is not on sale until it is activated.</summary>
public sealed record CreatePlanCommand(PlanFields Fields) : ICommand<Guid>, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => null;
}

internal sealed class CreatePlanHandler(IPlanRepository plans) : ICommandHandler<CreatePlanCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = Plan.Create(command.Fields.Texts, command.Fields.Terms);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        if (await plans.IsProviderPriceTakenAsync(plan.Value, cancellationToken).ConfigureAwait(false))
        {
            return PlanErrors.ProviderPriceTaken;
        }

        plans.Add(plan.Value);
        return plan.Value.Id;
    }
}
