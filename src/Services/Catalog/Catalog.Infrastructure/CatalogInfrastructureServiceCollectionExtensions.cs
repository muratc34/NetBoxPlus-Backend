using Catalog.Application.Abstractions;
using Catalog.Domain.Genres;
using Catalog.Domain.Movies;
using Catalog.Domain.People;
using Catalog.Domain.Shows;
using Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace Catalog.Infrastructure;

public static class CatalogInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database (from <c>ConnectionStrings:Database</c>), the repositories, the read
    /// model and RabbitMQ messaging (from the <c>Messaging</c> section).
    /// </summary>
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(CatalogDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{CatalogDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddCatalogPersistence(connectionString);
        services.AddRabbitMqMessaging();

        return services;
    }

    /// <summary>The database and repositories; the integration tests use it on its own.</summary>
    internal static IServiceCollection AddCatalogPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPersistence<CatalogDbContext>(connectionString);
        services.AddScoped<ICatalogReadModel>(provider => provider.GetRequiredService<CatalogDbContext>());
        services.AddScoped<IMovieRepository, MovieRepository>();
        services.AddScoped<ISeriesRepository, SeriesRepository>();
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IGenreRepository, GenreRepository>();

        return services;
    }
}
