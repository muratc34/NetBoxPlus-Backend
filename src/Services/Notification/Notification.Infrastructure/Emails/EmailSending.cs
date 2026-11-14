using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Notification.Infrastructure.Persistence;

namespace Notification.Infrastructure.Emails;

public sealed class NotificationOptions
{
    public const string SectionName = "Notification";

    /// <summary>The customer web app, where the links in e-mails go.</summary>
    public Uri? WebBaseUrl { get; set; }

    internal bool IsValid => WebBaseUrl is { IsAbsoluteUri: true } address && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp);
}

internal sealed class EmailLinks(IOptions<NotificationOptions> options) : IEmailLinks
{
    public Uri To(string relative)
    {
        var root = options.Value.WebBaseUrl!.AbsoluteUri;
        return new Uri(new Uri(root.EndsWith('/') ? root : root + "/"), relative);
    }
}

/// <summary>
/// Sends the e-mails that are due, a few at a time. They are taken with <c>FOR UPDATE SKIP LOCKED</c>
/// and a lease, so several workers never send the same e-mail and a dead worker's e-mails are taken
/// again. The address is read now: a user's registration may reach this service after the e-mail.
/// </summary>
internal sealed partial class EmailDispatcher(
    NotificationDbContext context,
    IEmailSender sender,
    TimeProvider timeProvider,
    ILogger<EmailDispatcher> logger)
{
    public const int BatchSize = 20;

    /// <summary>Longer than sending a batch takes; a worker that is still alive finishes well within it.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>How long a failed e-mail keeps its content, so that an admin can send it again.</summary>
    public static readonly TimeSpan FailedContentKept = TimeSpan.FromDays(7);

    public const string NoAddressYet = "No address for the user yet.";

    /// <summary>Sends the e-mails that are due; returns how many were taken.</summary>
    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseEnd = now + Lease;
        var ids = await context.Database.SqlQuery<Guid>(
            $"""
            UPDATE "OutgoingEmails" SET "LeasedUntil" = {leaseEnd}
            WHERE "Id" IN (
                SELECT "Id" FROM "OutgoingEmails"
                WHERE "Status" = 'Pending' AND "NextAttemptAt" <= {now} AND ("LeasedUntil" IS NULL OR "LeasedUntil" < {now})
                ORDER BY "NextAttemptAt"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var id in ids)
        {
            await SendAsync(id, cancellationToken).ConfigureAwait(false);
        }

        return ids.Count;
    }

    /// <summary>Empties the content of e-mails that failed long enough ago.</summary>
    public Task<int> ForgetOldFailuresAsync(CancellationToken cancellationToken)
    {
        var before = timeProvider.GetUtcNow() - FailedContentKept;
        return context.Emails
            .Where(email => email.Status == EmailStatus.Failed && email.HtmlBody != null && email.ModifiedAt < before)
            .ExecuteUpdateAsync(setters => setters.SetProperty(email => email.HtmlBody, (string?)null).SetProperty(email => email.TextBody, (string?)null), cancellationToken);
    }

    private async Task SendAsync(Guid id, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var email = await context.Emails.FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        if (email is null || email.Status != EmailStatus.Pending || email.HtmlBody is null)
        {
            return;
        }

        var address = await context.Recipients.Where(recipient => recipient.Id == email.UserId).Select(recipient => recipient.Email)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        EmailDelivery? delivery = null;
        if (address is null)
        {
            email.MarkNotSent(NoAddressYet, now);
            LogNotSent(logger, email.Id, email.Attempts, NoAddressYet);
        }
        else
        {
            delivery = await sender.SendAsync(new EmailMessage(address, email.Subject, email.HtmlBody, email.TextBody ?? string.Empty), cancellationToken).ConfigureAwait(false);
            switch (delivery.Outcome)
            {
                case EmailDeliveryOutcome.Sent:
                    email.MarkSent(now);
                    LogSent(logger, email.Id, email.Kind);
                    break;
                case EmailDeliveryOutcome.Rejected:
                    email.MarkRejected(delivery.Detail ?? "Rejected");
                    LogRejected(logger, email.Id, email.LastError!);
                    break;
                default:
                    email.MarkNotSent(delivery.Detail ?? "Not sent", now);
                    LogNotSent(logger, email.Id, email.Attempts, email.LastError!);
                    break;
            }
        }

        if (email.Status == EmailStatus.Failed && delivery is not { Outcome: EmailDeliveryOutcome.Rejected })
        {
            LogGaveUp(logger, email.Id, email.Attempts);
        }

        context.Entry(email).Property(OutgoingEmailConfiguration.LeasedUntil).CurrentValue = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent e-mail {EmailId} ({Kind})")]
    private static partial void LogSent(ILogger logger, Guid emailId, EmailKind kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-mail {EmailId} was not sent on attempt {Attempt}: {Reason}")]
    private static partial void LogNotSent(ILogger logger, Guid emailId, int attempt, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-mail {EmailId} was rejected: {Reason}")]
    private static partial void LogRejected(ILogger logger, Guid emailId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up on e-mail {EmailId} after {Attempts} attempts")]
    private static partial void LogGaveUp(ILogger logger, Guid emailId, int attempts);
}

/// <summary>Sends due e-mails continually; waits a few seconds when there are none.</summary>
internal sealed partial class EmailSendingService(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<EmailSendingService> logger) : BackgroundService
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = DateTimeOffset.MinValue;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var taken = 0;
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<EmailDispatcher>();
                    taken = await dispatcher.SendDueAsync(stoppingToken).ConfigureAwait(false);
                    if (timeProvider.GetUtcNow() >= nextCleanup)
                    {
                        await dispatcher.ForgetOldFailuresAsync(stoppingToken).ConfigureAwait(false);
                        nextCleanup = timeProvider.GetUtcNow() + CleanupInterval;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, exception);
                }

                if (taken < EmailDispatcher.BatchSize)
                {
                    await Task.Delay(Idle, timeProvider, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending e-mails failed; tried again in a few seconds")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
