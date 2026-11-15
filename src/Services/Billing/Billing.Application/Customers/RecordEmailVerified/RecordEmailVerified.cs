using Billing.Domain.Customers;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Customers.RecordEmailVerified;

/// <summary>
/// Records that the user's address is proven. When billing has not heard of the user yet (the
/// registration event may come second), the customer is made here from the address in this one.
/// Coming again changes nothing.
/// </summary>
public sealed record RecordEmailVerifiedCommand(Guid UserId, string? Email, DateTimeOffset VerifiedAt) : ICommand;

internal sealed class RecordEmailVerifiedValidator : IValidator<RecordEmailVerifiedCommand>
{
    private static readonly Error EmailInvalid = Error.Validation("Customer.EmailInvalid", $"The email is required, at most {BillingCustomer.EmailMaxLength} characters.");

    private static readonly Error UserRequired = Error.Validation("Customer.UserRequired", "The user id is required.");

    public IEnumerable<ValidationFailure> Validate(RecordEmailVerifiedCommand instance)
    {
        if (instance.UserId == Guid.Empty)
        {
            yield return new ValidationFailure(nameof(instance.UserId), UserRequired.Code, UserRequired.Message);
        }

        if (string.IsNullOrWhiteSpace(instance.Email) || instance.Email.Length > BillingCustomer.EmailMaxLength)
        {
            yield return new ValidationFailure(nameof(instance.Email), EmailInvalid.Code, EmailInvalid.Message);
        }
    }
}

internal sealed class RecordEmailVerifiedHandler(ICustomerRepository customers) : ICommandHandler<RecordEmailVerifiedCommand>
{
    public async Task<Result> Handle(RecordEmailVerifiedCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            // Racing the registration event here is settled by the unique index: the loser retries.
            customer = BillingCustomer.Create(command.UserId, command.Email!);
            customers.Add(customer);
        }

        customer.RecordEmailVerified(command.VerifiedAt);
        return Result.Success();
    }
}
