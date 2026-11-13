using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notification.Application.Abstractions;
using Notification.Domain.Recipients;
using Notification.Infrastructure.Email;
using Notification.Infrastructure.Persistence;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace Notification.Infrastructure;

public static class NotificationInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database (from <c>ConnectionStrings:Database</c>), the e-mail sender (from the
    /// <c>Email</c> section) and RabbitMQ messaging (from the <c>Messaging</c> section).
    /// </summary>
    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(NotificationDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{NotificationDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddNotificationPersistence(connectionString);
        services.AddEmail(configuration);
        services.AddRabbitMqMessaging();

        return services;
    }

    /// <summary>The database and repositories; the integration tests use it on its own.</summary>
    internal static IServiceCollection AddNotificationPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPersistence<NotificationDbContext>(connectionString);
        services.AddScoped<IRecipientRepository, RecipientRepository>();

        return services;
    }

    /// <summary>SMTP or Brevo, as configured; the service does not start with settings it cannot send with.</summary>
    internal static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(options => options.Problem is null, "The Email settings are not valid: a sender, and SMTP host and port or a Brevo key.")
            .ValidateOnStart();

        var provider = configuration.GetSection(EmailOptions.SectionName).GetValue<EmailProvider?>(nameof(EmailOptions.Provider)) ?? EmailProvider.Smtp;
        if (provider == EmailProvider.Brevo)
        {
            services.AddHttpClient<IEmailSender, BrevoEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(30));
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
