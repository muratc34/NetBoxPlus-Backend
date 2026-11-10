using Billing.Domain.Plans;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Billing.Application.Plans.ChangePlanSale;

/// <summary>Puts a plan on sale. Doing it twice changes nothing.</summary>
public sealed record ActivatePlanCommand(Guid PlanId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => PlanId;
}

/// <summary>Takes a plan off sale; its subscribers keep their subscriptions. Doing it twice changes nothing.</summary>
public sealed record DeactivatePlanCommand(Guid PlanId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => PlanId;
}

internal sealed class ActivatePlanHandler(IPlanRepository plans) : ICommandHandler<ActivatePlanCommand>
{
    public async Task<Result> Handle(ActivatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(command.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return PlanErrors.NotFound;
        }

        plan.Activate();
        return Result.Success();
    }
}

internal sealed class DeactivatePlanHandler(IPlanRepository plans) : ICommandHandler<DeactivatePlanCommand>
{
    public async Task<Result> Handle(DeactivatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(command.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return PlanErrors.NotFound;
        }

        plan.Deactivate();
        return Result.Success();
    }
}
