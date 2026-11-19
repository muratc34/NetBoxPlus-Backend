using Shared.Domain;

namespace Shared.Application;

public static class DataErrors
{
    /// <summary>
    /// A save lost a race with another write (<see cref="DataConflictException"/>). Trying again
    /// sees the other write, so a message handler that gets it is retried rather than dropped.
    /// </summary>
    public static readonly Error Conflict = Error.Conflict(
        "Data.Conflict",
        "The change conflicts with another one made at the same time. Reload and try again.");

    /// <summary>
    /// No database connection could be had (<see cref="DataUnavailableException"/>); the request
    /// did not reach the database and can be sent again.
    /// </summary>
    public static readonly Error Unavailable = Error.Unavailable(
        "Data.Unavailable",
        "The service is temporarily unable to handle the request. Try again shortly.");
}
