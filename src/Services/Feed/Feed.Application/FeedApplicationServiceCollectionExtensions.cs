using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Feed.Application;

public static class FeedApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dispatcher and every handler and validator of the feed service, including the
    /// integration event handlers that decide which events it subscribes to.
    /// </summary>
    public static IServiceCollection AddFeedApplication(this IServiceCollection services)
        => services.AddCqrs(typeof(FeedApplicationServiceCollectionExtensions).Assembly);
}
