using Shared.Domain;

namespace Billing.Domain.Customers;

/// <summary>A registered user as billing knows them, and their customer record at the payment provider.</summary>
public sealed class BillingCustomer : AggregateRoot, IAuditableEntity
{
    public const int EmailMaxLength = 320;
    public const int ProviderCustomerIdMaxLength = 128;

    private BillingCustomer(Guid id, Guid userId, string email)
        : base(id)
    {
        UserId = userId;
        Email = email;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private BillingCustomer()
    {
    }

    public Guid UserId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    /// <summary>Set the first time the user goes to pay.</summary>
    public string? ProviderCustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static BillingCustomer Create(Guid userId, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return new BillingCustomer(Guid.CreateVersion7(), userId, email);
    }

    /// <summary>Records the provider's customer; a customer is never moved to a different one.</summary>
    public Result LinkProvider(string providerCustomerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCustomerId);

        if (ProviderCustomerId is not null && ProviderCustomerId != providerCustomerId)
        {
            return CustomerErrors.ProviderMismatch;
        }

        ProviderCustomerId = providerCustomerId;
        return Result.Success();
    }
}

public static class CustomerErrors
{
    public static readonly Error NotReady = Error.Conflict("Customer.NotReady", "The account is not ready for billing yet. Try again shortly.");

    public static readonly Error ProviderMismatch = Error.Conflict("Customer.ProviderMismatch", "The account is linked to a different payment customer.");
}
