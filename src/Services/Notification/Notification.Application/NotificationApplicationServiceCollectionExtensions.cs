using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Notification.Application;

public static class NotificationApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler and validator of the notification service.</summary>
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(NotificationApplicationServiceCollectionExtensions).Assembly);
        return services;
    }
}
