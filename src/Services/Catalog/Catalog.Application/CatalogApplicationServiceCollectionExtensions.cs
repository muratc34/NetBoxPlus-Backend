using Catalog.Application.Content;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Catalog.Application;

public static class CatalogApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher, every handler and validator of the catalog service, and their helpers.</summary>
    public static IServiceCollection AddCatalogApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(CatalogApplicationServiceCollectionExtensions).Assembly);
        services.AddScoped<ContentEditor>();
        services.AddScoped<ContentReader>();
        return services;
    }
}
