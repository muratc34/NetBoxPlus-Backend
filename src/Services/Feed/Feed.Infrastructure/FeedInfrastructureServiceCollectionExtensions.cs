using Feed.Application.Abstractions;
using Feed.Domain.Items;
using Feed.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace Feed.Infrastructure;

public static class FeedInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database (from <c>ConnectionStrings:Database</c>), the repositories and the
    /// read model, and RabbitMQ messaging (from the <c>Messaging</c> section), which consumes the
    /// events the application handles.
    /// </summary>
    public static IServiceCollection AddFeedInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(FeedDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{FeedDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddPersistence<FeedDbContext>(connectionString);
        services.AddScoped<IFeedReadModel>(provider => provider.GetRequiredService<FeedDbContext>());
        services.AddScoped<IFeedItemRepository, FeedItemRepository>();

        services.AddRabbitMqMessaging();

        return services;
    }
}
