using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;
using Streaming.Infrastructure.Persistence;

namespace Streaming.Infrastructure;

public static class StreamingInfrastructureServiceCollectionExtensions
{
    /// <summary>Registers the database (from <c>ConnectionStrings:Database</c>) and RabbitMQ messaging (from the <c>Messaging</c> section).</summary>
    public static IServiceCollection AddStreamingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(StreamingDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{StreamingDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddStreamingPersistence(connectionString);
        services.AddRabbitMqMessaging();

        return services;
    }

    /// <summary>The database; the integration tests use it on its own.</summary>
    internal static IServiceCollection AddStreamingPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPersistence<StreamingDbContext>(connectionString);

        return services;
    }
}
