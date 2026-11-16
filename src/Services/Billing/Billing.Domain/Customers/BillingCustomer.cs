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

    /// <summary>The revision of <see cref="Email"/> as auth numbers it: 1 for the registration address, higher after each change.</summary>
    public int EmailRevision { get; private set; } = 1;

    /// <summary>When the address was first proven, as auth reported it; a subscription needs it.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    /// <summary>Set the first time the user goes to pay.</summary>
    public string? ProviderCustomerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static BillingCustomer Create(Guid userId, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return new BillingCustomer(Guid.CreateVersion7(), userId, email);
    }

    /// <summary>
    /// Takes the address of a later revision; an older or the same revision changes nothing, so
    /// changes may arrive in any order.
    /// </summary>
    /// <returns>Whether the address was taken.</returns>
    public bool ChangeEmail(string email, int revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        if (revision <= EmailRevision)
        {
            return false;
        }

        Email = email;
        EmailRevision = revision;
        return true;
    }

    /// <summary>Records the verification; the first time stays, whatever order the reports come in.</summary>
    public void RecordEmailVerified(DateTimeOffset verifiedAt)
    {
        if (EmailVerifiedAt is null || verifiedAt < EmailVerifiedAt)
        {
            EmailVerifiedAt = verifiedAt;
        }
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

    /// <summary>Paying needs an address that reaches the customer: receipts and payment notices go there.</summary>
    public static readonly Error EmailNotVerified = Error.Forbidden(
        "Checkout.EmailNotVerified",
        "Verify your e-mail address before starting a subscription.");

    public static readonly Error ProviderMismatch = Error.Conflict("Customer.ProviderMismatch", "The account is linked to a different payment customer.");
}
