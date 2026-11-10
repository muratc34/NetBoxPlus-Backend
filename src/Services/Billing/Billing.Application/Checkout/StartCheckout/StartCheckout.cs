using Billing.Application.Abstractions;
using Billing.Domain.Customers;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Checkout.StartCheckout;

/// <summary>Opens a checkout page for a plan on sale. The subscription itself starts when the provider says it is paid.</summary>
public sealed record StartCheckoutCommand(Guid UserId, Guid PlanId, string IdempotencyKey) : ICommand<CheckoutSession>;

public sealed record CheckoutSession(Uri Url);

internal sealed class StartCheckoutValidator : IValidator<StartCheckoutCommand>
{
    private static readonly Error PlanRequired = Error.Validation("Checkout.PlanRequired", "The plan id is required.");

    public IEnumerable<ValidationFailure> Validate(StartCheckoutCommand instance)
    {
        if (instance.PlanId == Guid.Empty)
        {
            yield return new ValidationFailure(nameof(instance.PlanId), PlanRequired.Code, PlanRequired.Message);
        }
    }
}

internal sealed class StartCheckoutHandler(
    IPlanRepository plans,
    ICustomerRepository customers,
    ISubscriptionRepository subscriptions,
    IPaymentGateway gateway) : ICommandHandler<StartCheckoutCommand, CheckoutSession>
{
    public async Task<Result<CheckoutSession>> Handle(StartCheckoutCommand command, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(command.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan is not { IsActive: true })
        {
            return PlanErrors.NotFound;
        }

        var customer = await customers.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return CustomerErrors.NotReady;
        }

        if (await subscriptions.HasLiveAsync(command.UserId, cancellationToken).ConfigureAwait(false))
        {
            return SubscriptionErrors.AlreadyActive;
        }

        if (customer.ProviderCustomerId is null)
        {
            var created = await gateway.EnsureCustomerAsync(command.UserId, customer.Email, cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return created.Error;
            }

            var linked = customer.LinkProvider(created.Value);
            if (linked.IsFailure)
            {
                return linked.Error;
            }
        }

        var url = await gateway
            .CreateCheckoutAsync(new CheckoutRequest(command.UserId, plan.Id, customer.ProviderCustomerId!, plan.ProviderPriceId, command.IdempotencyKey), cancellationToken)
            .ConfigureAwait(false);

        return url.IsFailure ? url.Error : new CheckoutSession(url.Value);
    }
}
