using Auth.Contracts;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;

namespace Billing.IntegrationTests;

/// <summary>The consumer as the messaging layer runs it: through the inbox, with the handler's full name as its key.</summary>
[Collection(nameof(BillingDatabaseCollection))]
public sealed class CustomerTests(BillingDatabaseFixture database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARegisteredUser_BecomesACustomer()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.NewGuid();

        Assert.True(await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, "new@example.com", DateTimeOffset.UnixEpoch)));

        var customer = Assert.Single(await CustomersAsync(services));
        Assert.Equal((user, "new@example.com", (string?)null), (customer.UserId, customer.Email, customer.ProviderCustomerId));
    }

    [Fact]
    public async Task TheSameMessage_IsHandledOnce()
    {
        await using var services = await database.CreateServicesAsync();
        var message = Guid.NewGuid();
        var registered = new UserRegistered(Guid.NewGuid(), "twice@example.com", DateTimeOffset.UnixEpoch);

        Assert.True(await DeliverAsync(services, message, registered));
        Assert.False(await DeliverAsync(services, message, registered));

        Assert.Single(await CustomersAsync(services));
    }

    [Fact]
    public async Task TheSameUser_UnderAnotherMessage_StaysOneCustomer()
    {
        await using var services = await database.CreateServicesAsync();
        var registered = new UserRegistered(Guid.NewGuid(), "again@example.com", DateTimeOffset.UnixEpoch);

        Assert.True(await DeliverAsync(services, Guid.NewGuid(), registered));
        Assert.True(await DeliverAsync(services, Guid.NewGuid(), registered, expectSuccess: true));

        Assert.Single(await CustomersAsync(services));
    }

    [Fact]
    public async Task AnUnusableMessage_IsRecorded_ButCreatesNothing()
    {
        await using var services = await database.CreateServicesAsync();
        var message = Guid.NewGuid();
        var broken = new UserRegistered(Guid.NewGuid(), " ", DateTimeOffset.UnixEpoch);

        Assert.True(await DeliverAsync(services, message, broken));
        Assert.False(await DeliverAsync(services, message, broken));

        Assert.Empty(await CustomersAsync(services));
    }

    private static async Task<bool> DeliverAsync(ServiceProvider services, Guid messageId, UserRegistered registered, bool expectSuccess = false)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<UserRegistered>>());
        Assert.Equal("Billing.Application.Customers.CreateCustomerOnUserRegistered", handler.GetType().FullName);

        return await scope.ServiceProvider.GetRequiredService<IInbox>()
            .ProcessOnceAsync(
                messageId,
                handler.GetType().FullName!,
                async token =>
                {
                    var result = await handler.Handle(registered, token);
                    Assert.True(!expectSuccess || result.IsSuccess, result.IsFailure ? result.Error.Code : null);
                    return result;
                },
                Cancellation);
    }

    private static async Task<List<Billing.Domain.Customers.BillingCustomer>> CustomersAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Customers.AsNoTracking().ToListAsync(Cancellation);
    }
}
