using Microsoft.Extensions.Logging;

namespace Auth.Migrator;

internal static partial class MigratorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "The database is up to date")]
    public static partial void UpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migrations: {Migrations}")]
    public static partial void Applying(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migrations")]
    public static partial void Applied(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "No administrator account is configured under {Section}; only the administrator role is kept up to date")]
    public static partial void NoAdministratorConfigured(ILogger logger, string section);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the administrator: role changed {RoleChanged}, account created {UserCreated}, role assigned {RoleAssigned}")]
    public static partial void Seeded(ILogger logger, bool roleChanged, bool userCreated, bool roleAssigned);

    [LoggerMessage(Level = LogLevel.Error, Message = "Seeding the administrator failed with {ErrorCode}")]
    public static partial void SeedFailed(ILogger logger, string errorCode);
}
