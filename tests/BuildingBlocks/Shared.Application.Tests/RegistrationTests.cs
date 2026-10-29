using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Shared.Application.Events;
using Shared.Application.Messaging;
using Shared.Application.Validation;

namespace Shared.Application.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void AddCqrs_RegistersHandlersValidatorsAndDomainEventHandlers()
    {
        var services = new ServiceCollection().AddCqrs(typeof(RegistrationTests).Assembly);

        Assert.Contains(services, d => d.ServiceType == typeof(ICommandHandler<PingCommand>) && d.ImplementationType == typeof(PingHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(IQueryHandler<GetItemQuery, string>));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IValidator<CreateItemCommand>)));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IDomainEventHandler<ItemCreated>)));
    }

    [Fact]
    public void AddCqrs_RegistersEverythingAsScoped()
    {
        var services = new ServiceCollection().AddCqrs(typeof(RegistrationTests).Assembly);

        Assert.All(
            services.Where(d => d.ServiceType != typeof(TimeProvider) && d.ServiceType != typeof(IntegrationEventSubscriptions)),
            d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    [Fact]
    public void AddCqrs_CalledTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();

        services.AddCqrs(typeof(RegistrationTests).Assembly);
        var count = services.Count;
        services.AddCqrs(typeof(RegistrationTests).Assembly);

        Assert.Equal(count, services.Count);
    }

    [Fact]
    public void AddCqrs_RegistersTheStandardBehaviorsInOrder()
    {
        var behaviors = new ServiceCollection()
            .AddCqrs(typeof(RegistrationTests).Assembly)
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType!.Name)
            .ToList();

        Assert.Equal(["LoggingBehavior`2", "ValidationBehavior`2", "TransactionBehavior`2"], behaviors);
    }

    [Fact]
    public void AddCqrs_WithoutAssemblies_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddCqrs());
    }

    [Fact]
    public void TwoHandlersForOneRequest_AreRejected()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => CqrsServiceCollectionExtensions.AddHandlers(
            services,
            [typeof(PingHandler), typeof(DuplicatePingHandler<int>)]));

        Assert.Contains(nameof(PingCommand), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddCqrs_RegistersIntegrationEventHandlersAndRecordsTheirEvents()
    {
        var services = new ServiceCollection().AddCqrs(typeof(RegistrationTests).Assembly);

        Assert.Contains(services, d => d.ServiceType == typeof(IIntegrationEventHandler<ItemShipped>)
            && d.ImplementationType == typeof(ItemShippedHandler));

        var subscriptions = Assert.Single(services, d => d.ServiceType == typeof(IntegrationEventSubscriptions)).ImplementationInstance;
        Assert.Equal([typeof(ItemShipped)], Assert.IsType<IntegrationEventSubscriptions>(subscriptions).EventTypes);
    }

    [Fact]
    public void Subscriptions_AreSharedWithAnInstanceRegisteredEarlier()
    {
        var services = new ServiceCollection();
        var early = CqrsServiceCollectionExtensions.GetOrAddSubscriptions(services);

        services.AddCqrs(typeof(RegistrationTests).Assembly);

        Assert.Contains(typeof(ItemShipped), early.EventTypes);
        Assert.Single(services, d => d.ServiceType == typeof(IntegrationEventSubscriptions));
    }

    [Fact]
    public void OpenGenericTypes_AreNotRegistered()
    {
        var services = new ServiceCollection().AddCqrs(typeof(RegistrationTests).Assembly);

        Assert.DoesNotContain(services, d => d.ImplementationType is { IsGenericType: true } type
            && type.GetGenericTypeDefinition() == typeof(DuplicatePingHandler<>));
    }
}
