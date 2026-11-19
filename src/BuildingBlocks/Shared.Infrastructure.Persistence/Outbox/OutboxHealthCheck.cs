using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Degraded while messages are given up on: the service still works, but events were lost on
/// the way out. Degraded answers 200, so traffic keeps flowing; the state shows in the response.
/// </summary>
internal sealed class OutboxHealthCheck(OutboxMetrics metrics) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(metrics.Latest is { Abandoned: > 0 } snapshot
            ? HealthCheckResult.Degraded($"{snapshot.Abandoned} outbox messages were given up on and will not be sent.")
            : HealthCheckResult.Healthy());
}
