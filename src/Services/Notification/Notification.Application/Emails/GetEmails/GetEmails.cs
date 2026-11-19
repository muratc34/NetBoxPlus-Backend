using Microsoft.EntityFrameworkCore;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Notification.Application.Emails.GetEmails;

/// <summary>E-mails for the admin panel, newest first. Addresses are not shown: they are the recipients'.</summary>
public sealed record GetEmailsQuery(EmailStatus? Status, Guid? UserId, EmailKind? Kind, int PageNumber, int PageSize) : IQuery<Page<EmailSummary>>;

public sealed record EmailSummary(Guid Id, Guid UserId, EmailKind Kind, EmailStatus Status, string Subject, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);

/// <param name="HasContent">Whether the body is still kept, so that a failed e-mail can be sent again.</param>
public sealed record EmailDetails(
    Guid Id,
    Guid UserId,
    EmailKind Kind,
    string SourceKey,
    EmailStatus Status,
    string Subject,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    string? LastError,
    bool HasContent,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

public sealed record GetEmailQuery(Guid EmailId) : IQuery<EmailDetails>;

internal sealed class GetEmailsValidator : IValidator<GetEmailsQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetEmailsQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

internal sealed class GetEmailsHandler(INotificationReadModel readModel) : IQueryHandler<GetEmailsQuery, Page<EmailSummary>>
{
    public async Task<Result<Page<EmailSummary>>> Handle(GetEmailsQuery query, CancellationToken cancellationToken)
    {
        var emails = readModel.Emails;
        if (query.Status is { } status)
        {
            emails = emails.Where(email => email.Status == status);
        }

        if (query.UserId is { } userId)
        {
            emails = emails.Where(email => email.UserId == userId);
        }

        if (query.Kind is { } kind)
        {
            emails = emails.Where(email => email.Kind == kind);
        }

        var totalCount = await emails.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await emails
            .OrderByDescending(email => email.Id)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .Select(email => new EmailSummary(email.Id, email.UserId, email.Kind, email.Status, email.Subject, email.Attempts, email.CreatedAt, email.SentAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Page<EmailSummary>(items, query.PageNumber, query.PageSize, totalCount);
    }
}

internal sealed class GetEmailHandler(INotificationReadModel readModel) : IQueryHandler<GetEmailQuery, EmailDetails>
{
    public async Task<Result<EmailDetails>> Handle(GetEmailQuery query, CancellationToken cancellationToken)
    {
        var email = await readModel.Emails
            .Where(item => item.Id == query.EmailId)
            .Select(item => new EmailDetails(
                item.Id, item.UserId, item.Kind, item.SourceKey, item.Status, item.Subject, item.Attempts,
                item.NextAttemptAt, item.LastError, item.HtmlBody != null, item.CreatedAt, item.SentAt))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return email is null ? OutgoingEmailErrors.NotFound : email;
    }
}
