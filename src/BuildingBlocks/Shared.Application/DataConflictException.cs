namespace Shared.Application;

/// <summary>
/// The database refused a save because of another write: a stale version, a duplicate of a unique
/// value or a reference to a row that just went away. Handlers check these first and report them
/// with their own errors; this covers the requests that race past those checks.
/// </summary>
public sealed class DataConflictException(Exception innerException)
    : Exception("The save conflicts with another change.", innerException);
