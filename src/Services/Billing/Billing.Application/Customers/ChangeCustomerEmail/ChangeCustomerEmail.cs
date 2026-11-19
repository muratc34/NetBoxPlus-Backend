using Billing.Application.Abstractions;
using Billing.Domain.Customers;
using Microsoft.Extensions.Logging;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Customers.ChangeCustomerEmail;

/// <summary>
/// Takes the user's new address, and puts it on the payment provider's customer too, where receipts
/// go. When billing has not heard of the user yet, the customer is made here with this address; the
/// registration that comes later then changes nothing. An older or repeated change changes nothing.
/// </summary>
public sealed record ChangeCustomerEmailCommand(Guid UserId, string? Email, int Revision) : ICommand;

internal sealed class ChangeCustomerEmailValidator : IValidator<ChangeCustomerEmailCommand>
{
    private static readonly Error EmailInvalid = Error.Validation("Customer.EmailInvalid", $"The email is required, at most {BillingCustomer.EmailMaxLength} characters.");

    private static readonly Error UserRequired = Error.Validation("Customer.UserRequired", "The user id is required.");

    private static readonly Error RevisionInvalid = Error.Validation("Customer.EmailRevisionInvalid", "A changed address has a revision above 1.");

    public IEnumerable<ValidationFailure> Validate(ChangeCustomerEmailCommand instance)
    {
        if (instance.UserId == Guid.Empty)
        {
            yield return new ValidationFailure(nameof(instance.UserId), UserRequired.Code, UserRequired.Message);
        }

        if (string.IsNullOrWhiteSpace(instance.Email) || instance.Email.Length > BillingCustomer.EmailMaxLength)
        {
            yield return new ValidationFailure(nameof(instance.Email), EmailInvalid.Code, EmailInvalid.Message);
        }

        if (instance.Revision < 2)
        {
            yield return new ValidationFailure(nameof(instance.Revision), RevisionInvalid.Code, RevisionInvalid.Message);
        }
    }
}

internal sealed partial class ChangeCustomerEmailHandler(
    ICustomerRepository customers,
    IPaymentGateway payments,
    ILogger<ChangeCustomerEmailHandler> logger) : ICommandHandler<ChangeCustomerEmailCommand>
{
    public async Task<Result> Handle(ChangeCustomerEmailCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            // Racing the registration event here is settled by the unique index: the loser retries.
            customer = BillingCustomer.Create(command.UserId, command.Email!);
            customers.Add(customer);
        }

        if (!customer.ChangeEmail(command.Email!, command.Revision) || customer.ProviderCustomerId is null)
        {
            return Result.Success();
        }

        var updated = await payments.UpdateCustomerEmailAsync(customer.ProviderCustomerId, command.Email!, cancellationToken).ConfigureAwait(false);
        if (updated.IsFailure && updated.Error.Type == ErrorType.Unavailable)
        {
            // Not saved: the message is tried again, and the provider gets the address then.
            throw new PaymentProviderUnavailableException("The payment provider could not be reached to update the customer's e-mail address.");
        }

        if (updated.IsFailure)
        {
            // The provider refused (its customer is gone, say): billing's own copy is what counts.
            LogProviderRefused(logger, customer.UserId, updated.Error.Code);
        }

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The payment provider refused the new e-mail address of user {UserId}'s customer ({ErrorCode}); billing keeps it")]
    private static partial void LogProviderRefused(ILogger logger, Guid userId, string errorCode);
}

/// <summary>
/// The payment provider could not be reached while handling an event: thrown, not returned, so that
/// the message is tried again instead of being recorded as handled.
/// </summary>
public sealed class PaymentProviderUnavailableException(string message) : Exception(message);
