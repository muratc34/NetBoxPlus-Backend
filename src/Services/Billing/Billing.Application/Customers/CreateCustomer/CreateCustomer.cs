using Billing.Domain.Customers;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Billing.Application.Customers.CreateCustomer;

/// <summary>Makes a registered user known to billing. Coming again for the same user is a success that changes nothing.</summary>
public sealed record CreateCustomerCommand(Guid UserId, string? Email) : ICommand;

internal sealed class CreateCustomerValidator : IValidator<CreateCustomerCommand>
{
    private static readonly Error EmailInvalid = Error.Validation("Customer.EmailInvalid", $"The email is required, at most {BillingCustomer.EmailMaxLength} characters.");

    private static readonly Error UserRequired = Error.Validation("Customer.UserRequired", "The user id is required.");

    public IEnumerable<ValidationFailure> Validate(CreateCustomerCommand instance)
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

internal sealed class CreateCustomerHandler(ICustomerRepository customers) : ICommandHandler<CreateCustomerCommand>
{
    public async Task<Result> Handle(CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        // A second delivery under a different message id; the unique index settles a race.
        if (await customers.ExistsForUserAsync(command.UserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        customers.Add(BillingCustomer.Create(command.UserId, command.Email!));
        return Result.Success();
    }
}
