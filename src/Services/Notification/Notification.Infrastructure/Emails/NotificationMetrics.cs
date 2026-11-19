using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notification.Domain.Emails;
using Notification.Infrastructure.Persistence;

namespace Notification.Infrastructure.Emails;

/// <summary>How a send attempt ended, as the <c>result</c> tag spells it.</summary>
internal static class EmailResults
{
    public const string Sent = "sent";

    /// <summary>Not sent this time (unreachable, throttled, no address yet); tried again later.</summary>
    public const string Retry = "retry";

    /// <summary>Refused by the provider; not tried again.</summary>
    public const string Rejected = "rejected";

    /// <summary>Not sent after every attempt.</summary>
    public const string GaveUp = "gave_up";
}

/// <summary>The e-mail queue as last counted.</summary>
internal sealed record EmailQueueSnapshot(long Pending, long Failed);

/// <summary>
/// The <c>NetBoxPlus.Notification</c> meter. Tags carry the kind of e-mail and the result, never
/// an address. The queue gauge reports the latest count taken by <see cref="EmailQueueSampler"/>.
/// </summary>
internal sealed class NotificationMetrics
{
    public const string MeterName = "NetBoxPlus.Notification";

    private readonly Counter<long> _attempts;
    private volatile EmailQueueSnapshot? _latest;

    public NotificationMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);

        _attempts = meter.CreateCounter<long>("netboxplus.emails.sent", "{email}", "Send attempts by kind of e-mail and result.");
        meter.CreateObservableGauge(
            "netboxplus.emails.queued",
            () => _latest is { } snapshot
                ? [new Measurement<long>(snapshot.Pending, new KeyValuePair<string, object?>("status", "pending")), new Measurement<long>(snapshot.Failed, new KeyValuePair<string, object?>("status", "failed"))]
                : Array.Empty<Measurement<long>>(),
            "{email}",
            "E-mails waiting to be sent, and those that failed for good.");
    }

    public EmailQueueSnapshot? Latest
    {
        get => _latest;
        set => _latest = value;
    }

    public void Attempted(EmailKind kind, string result) => _attempts.Add(1, new TagList { { "kind", kind.ToString() }, { "result", result } });
}

/// <summary>Counts the e-mail queue for the gauge every 30 seconds.</summary>
internal sealed partial class EmailQueueSampler(
    IServiceScopeFactory scopes,
    NotificationMetrics metrics,
    TimeProvider timeProvider,
    ILogger<EmailQueueSampler> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSampleFailed(logger, exception);
            }

            try
            {
                await Task.Delay(Interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task SampleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var emails = scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Emails.AsNoTracking();
        var pending = await emails.LongCountAsync(email => email.Status == EmailStatus.Pending, cancellationToken).ConfigureAwait(false);
        var failed = await emails.LongCountAsync(email => email.Status == EmailStatus.Failed, cancellationToken).ConfigureAwait(false);
        metrics.Latest = new EmailQueueSnapshot(pending, failed);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting the e-mail queue failed")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
