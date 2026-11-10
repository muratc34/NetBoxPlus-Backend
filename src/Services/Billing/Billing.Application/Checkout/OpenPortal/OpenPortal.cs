using Billing.Application.Abstractions;
using Billing.Domain.Customers;
using Billing.Domain.Subscriptions;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Billing.Application.Checkout.OpenPortal;

/// <summary>Opens the provider's page where a customer manages card, plan and cancellation.</summary>
public sealed record OpenPortalCommand(Guid UserId) : ICommand<PortalSession>;

public sealed record PortalSession(Uri Url);

internal sealed class OpenPortalHandler(ICustomerRepository customers, IPaymentGateway gateway) : ICommandHandler<OpenPortalCommand, PortalSession>
{
    public async Task<Result<PortalSession>> Handle(OpenPortalCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (customer?.ProviderCustomerId is not { } providerCustomerId)
        {
            return SubscriptionErrors.None;
        }

        var url = await gateway.CreatePortalAsync(providerCustomerId, cancellationToken).ConfigureAwait(false);
        return url.IsFailure ? url.Error : new PortalSession(url.Value);
    }
}
