using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Streaming.Application;

public static class StreamingApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler and validator of the streaming service.</summary>
    public static IServiceCollection AddStreamingApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(StreamingApplicationServiceCollectionExtensions).Assembly);
        return services;
    }
}
