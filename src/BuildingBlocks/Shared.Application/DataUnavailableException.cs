namespace Shared.Application;

/// <summary>
/// No connection to the database could be had: the connection pool stayed full for the whole wait,
/// the server could not be reached, or it refused the connection for lack of resources. Nothing
/// reached the database, so the request can simply be tried again a moment later.
/// </summary>
public sealed class DataUnavailableException(Exception innerException)
    : Exception("The database cannot be reached at the moment.", innerException);
