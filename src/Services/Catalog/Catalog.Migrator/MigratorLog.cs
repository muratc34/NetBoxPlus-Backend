using Microsoft.Extensions.Logging;

namespace Catalog.Migrator;

internal static partial class MigratorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "The database is up to date")]
    public static partial void UpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migrations: {Migrations}")]
    public static partial void Applying(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migrations")]
    public static partial void Applied(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Image storage is ready")]
    public static partial void ImagesReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The search index is ready")]
    public static partial void SearchReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The search engine could not be reached; the index is created when it is back")]
    public static partial void SearchUnreachable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuilt the search index with {Count} titles")]
    public static partial void Reindexed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Announced what can be watched of {Count} titles")]
    public static partial void Announced(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Announcing the titles failed with {ErrorCode}")]
    public static partial void AnnounceFailed(ILogger logger, string errorCode);
}
