namespace Billing.Application.Abstractions;

public enum IdempotencyOutcome
{
    /// <summary>The key is new (or its earlier use expired or was abandoned): go ahead.</summary>
    Started,

    /// <summary>The same request already succeeded; answer with its response.</summary>
    Replay,

    /// <summary>The key was used for a different request.</summary>
    KeyReused,

    /// <summary>The first request with this key is still running.</summary>
    InProgress,
}

public sealed record IdempotencyStart(IdempotencyOutcome Outcome, int? StatusCode = null, string? ResponseBody = null);

/// <summary>
/// Remembers requests by the client's Idempotency-Key, per user. Writes happen at once, outside the
/// request's transaction: of two concurrent requests with one key, exactly one may start.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyStart> StartAsync(Guid userId, string key, string requestHash, CancellationToken cancellationToken);

    /// <summary>Keeps a successful response, to be replayed for the same request.</summary>
    Task CompleteAsync(Guid userId, string key, int statusCode, string responseBody, CancellationToken cancellationToken);

    /// <summary>Forgets a request that did not succeed, so that the client can retry with the same key.</summary>
    Task ReleaseAsync(Guid userId, string key, CancellationToken cancellationToken);
}
