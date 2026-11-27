using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Streaming.Application.Abstractions;
using Streaming.Application.Playback.GetProgress;
using Shared.Application;
using Streaming.Application.Playback;
using Streaming.Application.Playback.GetPlaylists;
using Streaming.Application.Trailers;

namespace Streaming.Application;

public static class StreamingApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler and validator of the streaming service.</summary>
    public static IServiceCollection AddStreamingApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(StreamingApplicationServiceCollectionExtensions).Assembly);
        services.AddScoped<PlaybackGate>();
        services.AddScoped<ProfileAccess>();
        services.TryAddScoped<IUnlockedProfiles, NoUnlockedProfiles>();
        services.AddScoped<PlaylistSource>();
        services.AddScoped<TrailerSource>();
        services.AddScoped<PlaybackPositions>();
        services.AddSingleton<StreamingMetrics>();
        return services;
    }
}
