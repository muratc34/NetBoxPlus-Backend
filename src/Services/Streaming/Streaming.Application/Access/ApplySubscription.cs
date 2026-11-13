using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;
using Streaming.Domain.Access;

namespace Streaming.Application.Access;

/// <summary>
/// The billing service's news about a subscription. <paramref name="Quality"/> and
/// <paramref name="MaxScreens"/> come with an active status only. Sent by the event handlers, not by people.
/// </summary>
public sealed record ApplySubscriptionCommand(
    Guid SubscriptionId,
    Guid UserId,
    SubscriptionStatus Status,
    VideoQuality? Quality,
    int? MaxScreens,
    int Revision) : ICommand;

internal sealed class ApplySubscriptionValidator : IValidator<ApplySubscriptionCommand>
{
    private static readonly Error PlanRequired = Error.Validation("Access.PlanRequired", "An active subscription comes with its video quality and a positive number of screens.");

    public IEnumerable<ValidationFailure> Validate(ApplySubscriptionCommand instance)
    {
        if (instance.Status == SubscriptionStatus.Active && (instance.Quality is null || instance.MaxScreens is not > 0))
        {
            yield return new ValidationFailure(nameof(instance.Quality), PlanRequired.Code, PlanRequired.Message);
        }
    }
}

internal sealed class ApplySubscriptionHandler(ISubscriptionCopyRepository subscriptions) : ICommandHandler<ApplySubscriptionCommand>
{
    public async Task<Result> Handle(ApplySubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptions.GetByIdAsync(command.SubscriptionId, cancellationToken).ConfigureAwait(false);
        if (subscription is null)
        {
            // Two first messages at once: the primary key settles it, and the loser is retried.
            subscription = SubscriptionCopy.Create(command.SubscriptionId, command.UserId);
            subscriptions.Add(subscription);
        }

        if (command.Status == SubscriptionStatus.Active)
        {
            subscription.Activate(command.Quality!.Value, command.MaxScreens!.Value, command.Revision);
        }
        else
        {
            subscription.Stop(command.Status, command.Revision);
        }

        return Result.Success();
    }
}
