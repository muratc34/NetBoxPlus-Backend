using Microsoft.Extensions.DependencyInjection;
using Shared.Application;
using Streaming.Application.Playback;
using Streaming.Application.Playback.GetPlaylists;

namespace Streaming.Application;

public static class StreamingApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler and validator of the streaming service.</summary>
    public static IServiceCollection AddStreamingApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(StreamingApplicationServiceCollectionExtensions).Assembly);
        services.AddScoped<PlaybackGate>();
        services.AddScoped<PlaylistSource>();
        services.AddScoped<PlaybackPositions>();
        return services;
    }
}
