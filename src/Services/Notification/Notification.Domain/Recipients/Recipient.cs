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

    private Recipient(Guid userId, string email, int emailRevision)
        : base(userId)
    {
        Email = email;
        EmailRevision = emailRevision;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Recipient()
    {
    }

    public string Email { get; private set; } = string.Empty;

    /// <summary>The revision of <see cref="Email"/> as the account service numbers it: 1 for the registration address.</summary>
    public int EmailRevision { get; private set; } = 1;

    /// <summary>
    /// When the user's account was deleted. The row stays without the address, so that nothing is sent to
    /// the user any more and a late registration or change does not bring the address back.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <param name="emailRevision">1 for the registration address; higher when the first news of the user is a change.</param>
    public static Result<Recipient> Create(Guid userId, string? email, int emailRevision = 1)
    {
        if (userId == Guid.Empty)
        {
            return RecipientErrors.UserRequired;
        }

        var address = Normalize(email);
        if (address is null)
        {
            return RecipientErrors.EmailInvalid;
        }

        return new Recipient(userId, address, Math.Max(1, emailRevision));
    }

    /// <summary>
    /// Takes the address of a later revision; an older or the same revision changes nothing, so changes
    /// may arrive in any order.
    /// </summary>
    /// <returns>Whether the address was taken.</returns>
    public Result<bool> ChangeEmail(string? email, int revision)
    {
        var address = Normalize(email);
        if (address is null)
        {
            return RecipientErrors.EmailInvalid;
        }

        if (revision <= EmailRevision)
        {
            return false;
        }

        Email = address;
        EmailRevision = revision;
        return true;
    }

    /// <summary>Forgets the address; no later change applies, whatever order the events come in.</summary>
    public void Delete(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return;
        }

        Email = DeletedEmail(Id);
        EmailRevision = int.MaxValue;
        DeletedAt = now;
    }

    /// <summary>The address a deleted recipient keeps: it cannot receive mail, and nothing is sent to it.</summary>
    public static string DeletedEmail(Guid userId) => $"deleted-{userId:N}@deleted.invalid";

    private static string? Normalize(string? email)
    {
        var trimmed = email?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > EmailMaxLength || !trimmed.Contains('@', StringComparison.Ordinal) ? null : trimmed;
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

    Task<Recipient?> GetAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Recipient recipient);
}
