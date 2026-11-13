using Shared.Domain;

namespace Notification.Domain.Recipients;

/// <summary>
/// Where to write to a user: a copy of the account service's address, kept from its events. The id
/// is the user's id.
/// </summary>
public sealed class Recipient : AggregateRoot
{
    /// <summary>The longest address the account service accepts.</summary>
    public const int EmailMaxLength = 320;

    private Recipient(Guid userId, string email)
        : base(userId)
    {
        Email = email;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Recipient()
    {
    }

    public string Email { get; private set; } = string.Empty;

    public static Result<Recipient> Create(Guid userId, string? email)
    {
        if (userId == Guid.Empty)
        {
            return RecipientErrors.UserRequired;
        }

        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > EmailMaxLength || !trimmed.Contains('@', StringComparison.Ordinal))
        {
            return RecipientErrors.EmailInvalid;
        }

        return new Recipient(userId, trimmed);
    }
}

public static class RecipientErrors
{
    public static readonly Error UserRequired = Error.Validation("Recipient.UserRequired", "The user id is required.");

    public static readonly Error EmailInvalid = Error.Validation("Recipient.EmailInvalid", $"The e-mail address is required, at most {Recipient.EmailMaxLength} characters.");
}

public interface IRecipientRepository
{
    Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Recipient recipient);
}
