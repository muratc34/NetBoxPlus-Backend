using Shared.Domain;

namespace Shared.Application.Messaging;

/// <summary>
/// Makes message handling idempotent. Implemented by the persistence layer and called by the
/// consumer, so messaging never depends on a database library.
/// </summary>
public interface IInbox
{
    /// <summary>
    /// Runs <paramref name="work"/> unless <paramref name="consumer"/> already processed
    /// <paramref name="messageId"/>, and records the message as processed in the same save as the
    /// work's changes.
    /// </summary>
    /// <returns><see langword="false"/> when the message had already been processed and was skipped.</returns>
    /// <remarks>
    /// When the work returns a failed result the message is still recorded as processed — retrying
    /// would fail the same way — but the work's changes are discarded. When it throws, nothing is
    /// recorded and the exception propagates so the caller can retry.
    /// </remarks>
    Task<bool> ProcessOnceAsync(
        Guid messageId,
        string consumer,
        Func<CancellationToken, Task<Result>> work,
        CancellationToken cancellationToken = default);
}
