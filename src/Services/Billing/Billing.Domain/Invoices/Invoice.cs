using Shared.Domain;

namespace Billing.Domain.Invoices;

public enum InvoiceStatus
{
    Open,
    PaymentFailed,
    Paid,
    Void,
}

/// <summary>A payment of the invoice failed for the first time.</summary>
public sealed record InvoicePaymentFailedDomainEvent(Guid InvoiceId, Guid SubscriptionId, Guid UserId, long AmountMinor, string Currency) : IDomainEvent;

/// <summary>The only things kept about a card: enough for a customer to recognize it.</summary>
public sealed record CardSummary(string Brand, string Last4)
{
    public const int BrandMaxLength = 32;
    public const int Last4Length = 4;

    /// <summary>Refuses anything that is not exactly four digits, so a card number cannot be stored here by mistake.</summary>
    public static Result<CardSummary> Create(string? brand, string? last4)
    {
        var name = brand?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > BrandMaxLength
            || last4 is null || last4.Length != Last4Length || !last4.All(char.IsAsciiDigit))
        {
            return InvoiceErrors.CardInvalid;
        }

        return new CardSummary(name, last4);
    }
}

/// <summary>A mirror of an invoice at the payment provider. Paid is final: a late failure message cannot undo it.</summary>
public sealed class Invoice : AggregateRoot, IAuditableEntity
{
    public const int ProviderInvoiceIdMaxLength = 128;

    private Invoice(Guid id, Guid userId, Guid subscriptionId, string providerInvoiceId, long amountMinor, string currency, DateTimeOffset issuedAt)
        : base(id)
    {
        UserId = userId;
        SubscriptionId = subscriptionId;
        ProviderInvoiceId = providerInvoiceId;
        AmountMinor = amountMinor;
        Currency = currency;
        IssuedAt = issuedAt;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Invoice()
    {
    }

    public Guid UserId { get; private set; }

    public Guid SubscriptionId { get; private set; }

    public string ProviderInvoiceId { get; private set; } = string.Empty;

    public long AmountMinor { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Open;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public string? CardBrand { get; private set; }

    public string? CardLast4 { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Invoice Record(Guid userId, Guid subscriptionId, string providerInvoiceId, long amountMinor, string currency, DateTimeOffset issuedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerInvoiceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new Invoice(Guid.CreateVersion7(), userId, subscriptionId, providerInvoiceId, amountMinor, currency, issuedAt);
    }

    public void MarkPaid(DateTimeOffset paidAt, CardSummary? card)
    {
        if (Status == InvoiceStatus.Paid)
        {
            return;
        }

        Status = InvoiceStatus.Paid;
        PaidAt = paidAt;
        CardBrand = card?.Brand;
        CardLast4 = card?.Last4;
    }

    public void MarkPaymentFailed()
    {
        if (Status == InvoiceStatus.Open)
        {
            Status = InvoiceStatus.PaymentFailed;
            RaiseDomainEvent(new InvoicePaymentFailedDomainEvent(Id, SubscriptionId, UserId, AmountMinor, Currency));
        }
    }

    public void MarkVoid()
    {
        if (Status != InvoiceStatus.Paid)
        {
            Status = InvoiceStatus.Void;
        }
    }
}

public static class InvoiceErrors
{
    public static readonly Error CardInvalid = Error.Validation("Invoice.CardInvalid", "The card summary must be a brand and exactly four digits.");
}
