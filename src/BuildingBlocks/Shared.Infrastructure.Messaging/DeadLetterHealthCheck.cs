using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Degraded while the dead-letter queue holds messages: the service still works, but events were
/// not handled. Degraded answers 200, so traffic keeps flowing; the state shows in the response.
/// </summary>
internal sealed class DeadLetterHealthCheck(MessagingMetrics metrics) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(metrics.DeadLetters is { Messages: > 0 } count
            ? HealthCheckResult.Degraded($"{count.Messages} messages are in the dead-letter queue {count.Queue}.")
            : HealthCheckResult.Healthy());
}
