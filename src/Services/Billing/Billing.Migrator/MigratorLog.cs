using Microsoft.Extensions.Logging;

namespace Billing.Migrator;

internal static partial class MigratorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "The database is up to date")]
    public static partial void UpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migrations: {Migrations}")]
    public static partial void Applying(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migrations")]
    public static partial void Applied(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Announced {Count} subscriptions")]
    public static partial void Announced(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Announcing the subscriptions failed with {ErrorCode}")]
    public static partial void AnnounceFailed(ILogger logger, string errorCode);
}
