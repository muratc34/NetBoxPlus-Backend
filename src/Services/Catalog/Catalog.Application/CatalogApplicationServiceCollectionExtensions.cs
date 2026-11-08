using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Catalog.Application;

public static class CatalogApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler and validator of the catalog service.</summary>
    public static IServiceCollection AddCatalogApplication(this IServiceCollection services)
        => services.AddCqrs(typeof(CatalogApplicationServiceCollectionExtensions).Assembly);
}
