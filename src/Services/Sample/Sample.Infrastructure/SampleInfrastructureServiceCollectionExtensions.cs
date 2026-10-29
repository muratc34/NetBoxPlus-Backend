using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample.Application.Abstractions;
using Sample.Domain.Notes;
using Sample.Infrastructure.Persistence;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace Sample.Infrastructure;

public static class SampleInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database (from <c>ConnectionStrings:Database</c>), the repositories and the
    /// read model, and RabbitMQ messaging (from the <c>Messaging</c> section).
    /// </summary>
    public static IServiceCollection AddSampleInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(SampleDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{SampleDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddPersistence<SampleDbContext>(connectionString);
        services.AddScoped<ISampleReadModel>(provider => provider.GetRequiredService<SampleDbContext>());
        services.AddScoped<INoteRepository, NoteRepository>();

        services.AddRabbitMqMessaging();

        return services;
    }
}
