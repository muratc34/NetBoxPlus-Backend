namespace Auth.Contracts;

// Profile events carry the profile's Revision, which goes up with every change. Messages can
// arrive out of order: a consumer ignores one whose Revision is not higher than the last it
// applied for that profile.

/// <param name="MaxAgeRating"><c>All</c>, <c>Age7</c>, <c>Age13</c>, <c>Age16</c> or <c>Age18</c>.</param>
public sealed record ProfileCreated(Guid UserId, Guid ProfileId, string MaxAgeRating, int Revision, DateTimeOffset OccurredAt);

/// <param name="MaxAgeRating"><c>All</c>, <c>Age7</c>, <c>Age13</c>, <c>Age16</c> or <c>Age18</c>.</param>
public sealed record ProfileChanged(Guid UserId, Guid ProfileId, string MaxAgeRating, int Revision, DateTimeOffset OccurredAt);

public sealed record ProfileRemoved(Guid UserId, Guid ProfileId, int Revision, DateTimeOffset OccurredAt);
