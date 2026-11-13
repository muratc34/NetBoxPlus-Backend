using Catalog.Application.Abstractions;
using Azure.Storage.Blobs;
using Catalog.Domain.Genres;
using Catalog.Domain.Images;
using Catalog.Domain.Movies;
using Catalog.Domain.People;
using Catalog.Domain.Shows;
using Catalog.Infrastructure.Images;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Search;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace Catalog.Infrastructure;

public static class CatalogInfrastructureServiceCollectionExtensions
{
    /// <summary>Configuration key under <c>ConnectionStrings</c> for image storage.</summary>
    public const string ImagesConnectionStringName = "Images";

    /// <summary>Configuration key under <c>ConnectionStrings</c> for the search engine's address.</summary>
    public const string SearchConnectionStringName = "Search";

    /// <summary>
    /// Registers the database (from <c>ConnectionStrings:Database</c>), the repositories, the read
    /// model, image storage (from <c>ConnectionStrings:Images</c>) and RabbitMQ messaging (from the
    /// <c>Messaging</c> section).
    /// </summary>
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(CatalogDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{CatalogDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        var imagesConnectionString = configuration.GetConnectionString(ImagesConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ImagesConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        var searchUrl = configuration.GetConnectionString(SearchConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{SearchConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddCatalogPersistence(connectionString);
        services.AddCatalogImages(imagesConnectionString);
        services.AddCatalogSearch(searchUrl);
        services.AddHostedService<ImageCleanupService>();
        services.AddHostedService<SearchIndexingService>();
        services.AddRabbitMqMessaging();

        return services;
    }

    /// <summary>The database and repositories, without storage, search or messaging: what the migrator needs. The integration tests use it on its own too.</summary>
    public static IServiceCollection AddCatalogPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPersistence<CatalogDbContext>(connectionString);
        services.AddScoped<ICatalogReadModel>(provider => provider.GetRequiredService<CatalogDbContext>());
        services.AddScoped<IMovieRepository, MovieRepository>();
        services.AddScoped<ISeriesRepository, SeriesRepository>();
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IGenreRepository, GenreRepository>();
        services.AddScoped<IImageFileRepository, ImageFileRepository>();
        services.AddScoped<ISearchQueue, EfSearchQueue>();

        return services;
    }

    /// <summary>The search index and its indexer; the service still starts when it is unreachable.</summary>
    internal static IServiceCollection AddCatalogSearch(this IServiceCollection services, string url)
    {
        services.AddSingleton(new ElasticsearchClient(new Uri(url)));
        services.AddSingleton<ElasticSearchIndex>();
        services.AddSingleton<ISearchIndex>(provider => provider.GetRequiredService<ElasticSearchIndex>());
        services.AddSingleton<ISearchIndexWriter>(provider => provider.GetRequiredService<ElasticSearchIndex>());
        services.AddScoped<SearchDocumentBuilder>();
        services.AddScoped<SearchIndexer>();
        services.AddScoped<SearchReindexer>();

        return services;
    }

    /// <summary>Image storage and inspection; the integration tests use it on its own.</summary>
    internal static IServiceCollection AddCatalogImages(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new BlobServiceClient(connectionString));
        services.AddSingleton<IImageStorage, BlobImageStorage>();
        services.AddSingleton<IImageInspector, ImageSharpInspector>();
        services.AddScoped<ImageCleaner>();

        return services;
    }
}
