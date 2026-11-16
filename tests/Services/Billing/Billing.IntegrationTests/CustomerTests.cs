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

    [Fact]
    public async Task AVerifiedAddress_IsRecorded_OnTheCustomer()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.NewGuid();
        var verifiedAt = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, "me@example.com", DateTimeOffset.UnixEpoch));

        Assert.True(await DeliverVerifiedAsync(services, Guid.NewGuid(), new EmailVerified(user, "me@example.com", verifiedAt, verifiedAt)));

        var customer = Assert.Single(await CustomersAsync(services));
        Assert.Equal(verifiedAt, customer.EmailVerifiedAt);
    }

    [Fact]
    public async Task AVerificationBeforeTheRegistration_MakesTheCustomer_AndTheRegistrationChangesNothing()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.NewGuid();
        var verifiedAt = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

        Assert.True(await DeliverVerifiedAsync(services, Guid.NewGuid(), new EmailVerified(user, "early@example.com", verifiedAt, verifiedAt)));
        Assert.True(await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, "early@example.com", DateTimeOffset.UnixEpoch), expectSuccess: true));

        var customer = Assert.Single(await CustomersAsync(services));
        Assert.Equal((user, "early@example.com", (DateTimeOffset?)verifiedAt), (customer.UserId, customer.Email, customer.EmailVerifiedAt));
    }

    [Fact]
    public async Task AVerificationAgain_OrReplayed_KeepsTheFirstTime()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.NewGuid();
        var first = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        await DeliverVerifiedAsync(services, Guid.NewGuid(), new EmailVerified(user, "again@example.com", first, first));

        Assert.True(await DeliverVerifiedAsync(services, Guid.NewGuid(), new EmailVerified(user, "again@example.com", first.AddDays(1), first.AddDays(1), Replayed: true), expectSuccess: true));

        Assert.Equal(first, Assert.Single(await CustomersAsync(services)).EmailVerifiedAt);
    }

    [Fact]
    public async Task AVerificationWithoutAnAddress_IsRecorded_ButChangesNothing()
    {
        await using var services = await database.CreateServicesAsync();

        Assert.True(await DeliverVerifiedAsync(services, Guid.NewGuid(), new EmailVerified(Guid.NewGuid(), "", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)));

        Assert.Empty(await CustomersAsync(services));
    }

    [Fact]
    public async Task AChangedAddress_ReachesTheCustomer_AndThePaymentProvider()
    {
        var payments = new FakePaymentGateway();
        await using var services = await database.CreateServicesAsync(payments);
        var user = Guid.NewGuid();
        await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, "old@example.com", DateTimeOffset.UnixEpoch));
        await LinkProviderAsync(services, user, "cus_changed");

        Assert.True(await DeliverChangedAsync(services, Guid.NewGuid(), new EmailChanged(user, "new@example.com", 2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), expectSuccess: true));

        var customer = Assert.Single(await CustomersAsync(services));
        Assert.Equal(("new@example.com", 2), (customer.Email, customer.EmailRevision));
        Assert.Equal(("cus_changed", "new@example.com"), Assert.Single(payments.EmailUpdates));
    }

    [Fact]
    public async Task AnOlderChange_ArrivingLate_ChangesNothing()
    {
        var payments = new FakePaymentGateway();
        await using var services = await database.CreateServicesAsync(payments);
        var user = Guid.NewGuid();
        await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, "first@example.com", DateTimeOffset.UnixEpoch));
        await LinkProviderAsync(services, user, "cus_order");

        await DeliverChangedAsync(services, Guid.NewGuid(), new EmailChanged(user, "third@example.com", 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), expectSuccess: true);
        Assert.True(await DeliverChangedAsync(services, Guid.NewGuid(), new EmailChanged(user, "second@example.com", 2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), expectSuccess: true));

        var customer = Assert.Single(await CustomersAsync(services));
        Assert.Equal(("third@example.com", 3), (customer.Email, customer.EmailRevision));
        Assert.Equal(("cus_order", "third@example.com"), Assert.Single(payments.EmailUpdates));
    }

    [Fact]
    public async Task AChangeBeforeTheRegistration_MakesTheCustomer_WithoutCallingTheProvider()
    {
        var payments = new FakePaymentGateway();
        await using var services = await database.CreateServicesAsync(payments);
        var user = Guid.NewGuid();

        Assert.True(await DeliverChangedAsync(services, Guid.NewGuid(), new EmailChanged(user, "moved@example.com", 2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), expectSuccess: true));
        Assert.True(await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, "registered@example.com", DateTimeOffset.UnixEpoch), expectSuccess: true));

        var customer = Assert.Single(await CustomersAsync(services));
        Assert.Equal(("moved@example.com", 2), (customer.Email, customer.EmailRevision));
        Assert.Empty(payments.EmailUpdates);
    }

    [Fact]
    public async Task AnUnreachableProvider_IsRetried_NotRecorded()
    {
        var payments = new FakePaymentGateway();
        await using var services = await database.CreateServicesAsync(payments);
        var user = Guid.NewGuid();
        var message = Guid.NewGuid();
        var changed = new EmailChanged(user, "retry@example.com", 2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, "before@example.com", DateTimeOffset.UnixEpoch));
        await LinkProviderAsync(services, user, "cus_retry");
        payments.FailWith = Billing.Application.Abstractions.PaymentErrors.Unavailable;

        await Assert.ThrowsAsync<Billing.Application.Customers.ChangeCustomerEmail.PaymentProviderUnavailableException>(() => DeliverChangedAsync(services, message, changed));
        Assert.Equal("before@example.com", Assert.Single(await CustomersAsync(services)).Email);

        payments.Reset();
        Assert.True(await DeliverChangedAsync(services, message, changed, expectSuccess: true));
        Assert.Equal("retry@example.com", Assert.Single(await CustomersAsync(services)).Email);
        Assert.Equal(("cus_retry", "retry@example.com"), Assert.Single(payments.EmailUpdates));
    }

    [Fact]
    public async Task AProviderThatRefuses_LeavesBillingsOwnCopyChanged()
    {
        var payments = new FakePaymentGateway { FailWith = Billing.Application.Abstractions.PaymentErrors.ProviderRejected };
        await using var services = await database.CreateServicesAsync(payments);
        var user = Guid.NewGuid();
        await DeliverAsync(services, Guid.NewGuid(), new UserRegistered(user, "before@example.com", DateTimeOffset.UnixEpoch));
        await LinkProviderAsync(services, user, "cus_gone");

        Assert.True(await DeliverChangedAsync(services, Guid.NewGuid(), new EmailChanged(user, "kept@example.com", 2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), expectSuccess: true));

        Assert.Equal("kept@example.com", Assert.Single(await CustomersAsync(services)).Email);
    }

    private static async Task LinkProviderAsync(ServiceProvider services, Guid user, string providerCustomerId)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Customers
            .Where(customer => customer.UserId == user)
            .ExecuteUpdateAsync(setters => setters.SetProperty(customer => customer.ProviderCustomerId, providerCustomerId), Cancellation);
    }

    private static async Task<bool> DeliverChangedAsync(ServiceProvider services, Guid messageId, EmailChanged changed, bool expectSuccess = false)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmailChanged>>());
        Assert.Equal("Billing.Application.Customers.ChangeCustomerEmailOnEmailChanged", handler.GetType().FullName);

        return await scope.ServiceProvider.GetRequiredService<IInbox>()
            .ProcessOnceAsync(
                messageId,
                handler.GetType().FullName!,
                async token =>
                {
                    var result = await handler.Handle(changed, token);
                    Assert.True(!expectSuccess || result.IsSuccess, result.IsFailure ? result.Error.Code : null);
                    return result;
                },
                Cancellation);
    }

    private static async Task<bool> DeliverVerifiedAsync(ServiceProvider services, Guid messageId, EmailVerified verified, bool expectSuccess = false)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmailVerified>>());
        Assert.Equal("Billing.Application.Customers.RecordEmailVerifiedOnEmailVerified", handler.GetType().FullName);

        return await scope.ServiceProvider.GetRequiredService<IInbox>()
            .ProcessOnceAsync(
                messageId,
                handler.GetType().FullName!,
                async token =>
                {
                    var result = await handler.Handle(verified, token);
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
