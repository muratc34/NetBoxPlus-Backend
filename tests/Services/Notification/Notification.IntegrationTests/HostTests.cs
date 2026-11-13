using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notification.Application;
using Notification.Infrastructure;

namespace Notification.IntegrationTests;

public sealed class HostTests
{
    [Fact]
    public void TheService_StartsWithItsRegistrationsChecked_AsInDevelopment()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=nowhere",
            ["Email:From"] = "no-reply@netboxplus.test",
            ["Email:Smtp:Host"] = "localhost",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddNotificationApplication();
        services.AddNotificationInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider);
    }
}
