using Billing.Application.Abstractions;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure.Idempotency;

/// <summary>
/// Immediate, atomic SQL: these writes must hold whatever the request's own transaction does, and
/// of two concurrent requests with one key only one may get through.
/// </summary>
internal sealed class EfIdempotencyStore(BillingDbContext context, TimeProvider timeProvider) : IIdempotencyStore
{
    /// <summary>After this a key can be used for anything again.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>A request still unfinished after this is taken to have died with its process.</summary>
    public static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(2);

    public async Task<IdempotencyStart> StartAsync(Guid userId, string key, string requestHash, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var abandoned = now - Abandoned;
        var expired = now - Retention;

        // Takes the key when it is free, expired, or held by a request that never finished.
        var taken = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "IdempotencyRequests" ("UserId", "Key", "RequestHash", "Completed", "StatusCode", "ResponseBody", "StartedAt")
            VALUES ({userId}, {key}, {requestHash}, FALSE, NULL, NULL, {now})
            ON CONFLICT ("UserId", "Key") DO UPDATE
            SET "RequestHash" = EXCLUDED."RequestHash", "Completed" = FALSE, "StatusCode" = NULL, "ResponseBody" = NULL, "StartedAt" = EXCLUDED."StartedAt"
            WHERE "IdempotencyRequests"."StartedAt" < {expired}
               OR (NOT "IdempotencyRequests"."Completed" AND "IdempotencyRequests"."StartedAt" < {abandoned})
            """,
            cancellationToken).ConfigureAwait(false);

        if (taken == 1)
        {
            return new IdempotencyStart(IdempotencyOutcome.Started);
        }

        var existing = await context.Set<IdempotencyRequest>().AsNoTracking()
            .FirstOrDefaultAsync(request => request.UserId == userId && request.Key == key, cancellationToken)
            .ConfigureAwait(false);

        return existing switch
        {
            // Released between the two statements: let the client try again.
            null => new IdempotencyStart(IdempotencyOutcome.InProgress),
            { RequestHash: var hash } when hash != requestHash => new IdempotencyStart(IdempotencyOutcome.KeyReused),
            { Completed: true } => new IdempotencyStart(IdempotencyOutcome.Replay, existing.StatusCode, existing.ResponseBody),
            _ => new IdempotencyStart(IdempotencyOutcome.InProgress),
        };
    }

    public Task CompleteAsync(Guid userId, string key, int statusCode, string responseBody, CancellationToken cancellationToken)
        => context.Set<IdempotencyRequest>()
            .Where(request => request.UserId == userId && request.Key == key)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(request => request.Completed, true)
                    .SetProperty(request => request.StatusCode, statusCode)
                    .SetProperty(request => request.ResponseBody, responseBody),
                cancellationToken);

    public Task ReleaseAsync(Guid userId, string key, CancellationToken cancellationToken)
        => context.Set<IdempotencyRequest>()
            .Where(request => request.UserId == userId && request.Key == key && !request.Completed)
            .ExecuteDeleteAsync(cancellationToken);
}
