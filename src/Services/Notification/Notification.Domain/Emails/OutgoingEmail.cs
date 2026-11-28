using Shared.Domain;

namespace Notification.Domain.Emails;

/// <summary>What the e-mail is about; with its source key, it is sent once.</summary>
public enum EmailKind
{
    Welcome,
    EmailVerification,
    PasswordReset,
    PasswordChanged,
    SubscriptionStarted,
    SubscriptionResumed,
    PaymentFailed,
    SubscriptionEnded,

    /// <summary>The link that moves the account to a new address; sent to that address.</summary>
    EmailChangeVerification,

    /// <summary>The notice that the account moved; sent to the address it moved from.</summary>
    EmailChanged,

    /// <summary>The last e-mail to a deleted account, to the address it had.</summary>
    AccountDeleted,

    /// <summary>A sign-in from a device not seen lately; the only kind the user can turn off.</summary>
    NewSignIn,
}

public enum EmailStatus
{
    Pending,
    Sent,

    /// <summary>Refused by the provider, or not sent after every attempt.</summary>
    Failed,
}

/// <summary>
/// One e-mail to one user, waiting to be sent or sent. The content is made when it is queued and
/// emptied once it is sent or has failed for good: a link with a token in it, or what a user was told,
/// does not stay longer than needed. The address is read when it is sent, so an e-mail can be queued
/// before the account service's registration has reached this service, unless the e-mail must go to
/// another address than the user's current one (<see cref="ToAddress"/>).
/// </summary>
public sealed class OutgoingEmail : AggregateRoot, IAuditableEntity
{
    public const int SubjectMaxLength = 200;
    public const int SourceKeyMaxLength = 100;
    public const int LastErrorMaxLength = 500;
    public const int UnsubscribeUrlMaxLength = 500;

    /// <summary>Attempts before an e-mail the provider keeps turning away is given up: about a day.</summary>
    public const int MaxAttempts = 8;

    /// <summary>Wait after each failed attempt; the last one repeats.</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(12),
    ];

    private OutgoingEmail(Guid id, Guid userId, EmailKind kind, string sourceKey, EmailContent content, string? toAddress, Uri? unsubscribeUrl, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        ToAddress = toAddress;
        UnsubscribeUrl = unsubscribeUrl?.AbsoluteUri;
        Kind = kind;
        SourceKey = sourceKey;
        Subject = content.Subject;
        HtmlBody = content.HtmlBody;
        TextBody = content.TextBody;
        NextAttemptAt = now;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private OutgoingEmail()
    {
    }

    public Guid UserId { get; private set; }

    public EmailKind Kind { get; private set; }

    /// <summary>What it was sent for (the user, an invoice, a token); unique with <see cref="Kind"/>.</summary>
    public string SourceKey { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string? HtmlBody { get; private set; }

    public string? TextBody { get; private set; }

    /// <summary>
    /// The address fixed when it was queued, for an e-mail that does not go to the user's current
    /// address: a change link to the new one, a notice to the old one. Emptied with the content.
    /// </summary>
    public string? ToAddress { get; private set; }

    /// <summary>
    /// Where a mail program turns this kind of e-mail off in one click (the <c>List-Unsubscribe</c> header); only on
    /// e-mails the user may turn off. Emptied with the content.
    /// </summary>
    public string? UnsubscribeUrl { get; private set; }

    public EmailStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    /// <param name="toAddress">Only when the e-mail must not go to the user's current address.</param>
    /// <param name="unsubscribeUrl">Only on an e-mail the user may turn off.</param>
    public static Result<OutgoingEmail> Queue(Guid userId, EmailKind kind, string sourceKey, EmailContent content, DateTimeOffset now, string? toAddress = null, Uri? unsubscribeUrl = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (userId == Guid.Empty)
        {
            return OutgoingEmailErrors.UserRequired;
        }

        if (string.IsNullOrWhiteSpace(sourceKey) || sourceKey.Length > SourceKeyMaxLength)
        {
            return OutgoingEmailErrors.SourceKeyInvalid;
        }

        if (string.IsNullOrWhiteSpace(content.Subject) || content.Subject.Length > SubjectMaxLength)
        {
            return OutgoingEmailErrors.SubjectInvalid;
        }

        if (toAddress is not null && (string.IsNullOrWhiteSpace(toAddress) || toAddress.Length > Recipients.Recipient.EmailMaxLength || !toAddress.Contains('@', StringComparison.Ordinal)))
        {
            return OutgoingEmailErrors.ToAddressInvalid;
        }

        if (unsubscribeUrl is not null && (!unsubscribeUrl.IsAbsoluteUri || unsubscribeUrl.AbsoluteUri.Length > UnsubscribeUrlMaxLength))
        {
            return OutgoingEmailErrors.UnsubscribeUrlInvalid;
        }

        return new OutgoingEmail(Guid.CreateVersion7(), userId, kind, sourceKey, content, toAddress?.Trim(), unsubscribeUrl, now);
    }

    /// <summary>The provider took it.</summary>
    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        Status = EmailStatus.Sent;
        SentAt = now;
        LastError = null;
        Forget();
    }

    /// <summary>It did not go out this time; tried again later, or given up after the last attempt.</summary>
    public void MarkNotSent(string reason, DateTimeOffset now)
    {
        Attempts++;
        LastError = Trim(reason);
        if (Attempts >= MaxAttempts)
        {
            Fail();
            return;
        }

        NextAttemptAt = now + RetryDelays[Math.Min(Attempts, RetryDelays.Count) - 1];
    }

    /// <summary>The provider refused it for good.</summary>
    public void MarkRejected(string reason)
    {
        Attempts++;
        LastError = Trim(reason);
        Fail();
    }

    /// <summary>An admin sends a failed e-mail again; possible only while its content is kept.</summary>
    public Result Retry(DateTimeOffset now)
    {
        if (Status != EmailStatus.Failed)
        {
            return OutgoingEmailErrors.NotFailed;
        }

        if (HtmlBody is null)
        {
            return OutgoingEmailErrors.ContentGone;
        }

        Status = EmailStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
        return Result.Success();
    }

    /// <summary>
    /// The content stays after a failure for a while, so that an admin can send it again; the cleanup
    /// of failed e-mails empties it.
    /// </summary>
    public void Forget()
    {
        HtmlBody = null;
        TextBody = null;
        ToAddress = null;
        UnsubscribeUrl = null;
    }

    private void Fail() => Status = EmailStatus.Failed;

    private static string Trim(string reason) => reason.Length > LastErrorMaxLength ? reason[..LastErrorMaxLength] : reason;
}

/// <summary>The rendered e-mail.</summary>
public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);

public static class OutgoingEmailErrors
{
    public static readonly Error NotFound = Error.NotFound("Email.NotFound", "The e-mail was not found.");

    public static readonly Error UserRequired = Error.Validation("Email.UserRequired", "The user id is required.");

    public static readonly Error SourceKeyInvalid = Error.Validation("Email.SourceKeyInvalid", $"The source key is required, at most {OutgoingEmail.SourceKeyMaxLength} characters.");

    public static readonly Error ToAddressInvalid = Error.Validation("Email.ToAddressInvalid", $"The address is at most {Recipients.Recipient.EmailMaxLength} characters.");

    public static readonly Error UnsubscribeUrlInvalid = Error.Validation("Email.UnsubscribeUrlInvalid", $"The unsubscribe address must be absolute, at most {OutgoingEmail.UnsubscribeUrlMaxLength} characters.");

    public static readonly Error SubjectInvalid = Error.Validation("Email.SubjectInvalid", $"The subject is required, at most {OutgoingEmail.SubjectMaxLength} characters.");

    public static readonly Error NotFailed = Error.Conflict("Email.NotFailed", "Only a failed e-mail can be sent again.");

    public static readonly Error ContentGone = Error.Conflict("Email.ContentGone", "The content of this e-mail is no longer kept; it cannot be sent again.");
}

public interface IOutgoingEmailRepository
{
    Task<OutgoingEmail?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(EmailKind kind, string sourceKey, CancellationToken cancellationToken);

    /// <summary>The user's e-mails, sent or not, to be forgotten with the account.</summary>
    Task<IReadOnlyList<OutgoingEmail>> GetForUserAsync(Guid userId, CancellationToken cancellationToken);

    void Remove(OutgoingEmail email);

    void Add(OutgoingEmail email);
}
