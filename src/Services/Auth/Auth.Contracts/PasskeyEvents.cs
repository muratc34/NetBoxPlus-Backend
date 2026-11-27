namespace Auth.Contracts;

/// <summary>A passkey was added to the account: the notification service tells the user.</summary>
public sealed record PasskeyAdded(Guid UserId, Guid PasskeyId, string Name, DateTimeOffset AddedAt, DateTimeOffset OccurredAt);

/// <summary>A passkey was removed from the account: the notification service tells the user.</summary>
public sealed record PasskeyRemoved(Guid UserId, Guid PasskeyId, string Name, DateTimeOffset RemovedAt, DateTimeOffset OccurredAt);
