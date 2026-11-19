using Auth.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Infrastructure.Persistence;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.IntegrationTests.Recipients;

/// <summary>The account service's registrations, through the inbox as the messaging layer delivers them.</summary>
[Collection(nameof(NotificationDatabaseCollection))]
public sealed class RecipientEventTests(NotificationDatabaseFixture database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARegistration_MakesARecipient_OnceHoweverOftenItComes()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var registered = new UserRegistered(user, "viewer@example.com", DateTimeOffset.UtcNow);
        var message = Guid.NewGuid();

        Assert.True((await DeliverAsync(services, message, registered)).Processed);
        Assert.False((await DeliverAsync(services, message, registered)).Processed);
        Assert.True((await DeliverAsync(services, Guid.NewGuid(), registered)).Result!.IsSuccess);

        await using var scope = services.CreateAsyncScope();
        var recipients = await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Recipients.Where(item => item.Id == user).ToListAsync(Cancellation);
        Assert.Equal("viewer@example.com", Assert.Single(recipients).Email);
    }

    [Fact]
    public async Task ARegistrationWithoutAnAddress_IsRefused_AndNotTriedAgain()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        var (processed, result) = await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, " ", DateTimeOffset.UtcNow));

        Assert.True(processed);
        Assert.Equal("Recipient.EmailInvalid", result!.Error.Code);
        await using var scope = services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Recipients.AnyAsync(item => item.Id == user, Cancellation));
    }

    [Fact]
    public void TheHandler_KeepsItsInboxKey()
    {
        var type = typeof(Notification.Application.NotificationApplicationServiceCollectionExtensions).Assembly
            .GetTypes().Single(candidate => typeof(IIntegrationEventHandler<UserRegistered>).IsAssignableFrom(candidate));

        Assert.Equal("Notification.Application.Recipients.AddRecipientOnUserRegistered", type.FullName);
    }

    private static async Task<(bool Processed, Result? Result)> DeliverAsync(ServiceProvider services, Guid messageId, UserRegistered registered)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<UserRegistered>>());
        Result? result = null;
        var processed = await scope.ServiceProvider.GetRequiredService<IInbox>().ProcessOnceAsync(
            messageId,
            handler.GetType().FullName!,
            async token => result = await handler.Handle(registered, token),
            Cancellation);
        return (processed, result);
    }
}
