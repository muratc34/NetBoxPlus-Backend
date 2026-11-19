using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Application.Behaviors;
using Shared.Application.Cqrs;
using Shared.Application.Events;
using Shared.Application.Messaging;
using Shared.Application.Validation;

namespace Shared.Application;

public static class CqrsServiceCollectionExtensions
{
    /// <summary>Handler kinds of which a request may have only one implementation.</summary>
    private static readonly Type[] SingleHandlerInterfaces =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
    ];

    /// <summary>Kinds that may have any number of implementations.</summary>
    private static readonly Type[] MultiHandlerInterfaces =
    [
        typeof(IDomainEventHandler<>),
        typeof(IValidator<>),
        typeof(IIntegrationEventHandler<>),
    ];

    /// <summary>
    /// Registers the dispatcher, the standard pipeline (logging, validation, transaction, audit — in
    /// that order, outermost first) and every handler, validator, domain event handler and integration
    /// event handler found in the given assemblies. All of them are scoped. The handled integration
    /// event types are recorded in <see cref="IntegrationEventSubscriptions"/>.
    /// </summary>
    /// <remarks>
    /// The transaction behavior needs an <see cref="IUnitOfWork"/> and the audit behavior an
    /// <see cref="Auditing.IAuditLog"/> and an <see cref="ICurrentUser"/>; the persistence and API layers
    /// register them. Additional behaviors registered afterwards run inside the standard ones.
    /// </remarks>
    public static IServiceCollection AddCqrs(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        if (assemblies.Length == 0)
        {
            throw new ArgumentException("At least one assembly is required.", nameof(assemblies));
        }

        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>)));

        // Inside the transaction, so that the entry is saved with the change. Applies to audited commands only.
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>)));

        AddHandlers(services, assemblies.Distinct().SelectMany(assembly => assembly.GetTypes()));

        return services;
    }

    internal static void AddHandlers(IServiceCollection services, IEnumerable<Type> types)
    {
        var subscriptions = GetOrAddSubscriptions(services);

        foreach (var type in types.Where(IsConcreteClass))
        {
            foreach (var serviceType in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = serviceType.GetGenericTypeDefinition();

                if (SingleHandlerInterfaces.Contains(definition))
                {
                    var existing = services.FirstOrDefault(d => d.ServiceType == serviceType);
                    if (existing is not null && existing.ImplementationType != type)
                    {
                        throw new InvalidOperationException(
                            $"'{serviceType}' has more than one handler: '{existing.ImplementationType}' and '{type}'. A request has exactly one handler.");
                    }

                    services.TryAddScoped(serviceType, type);
                }
                else if (MultiHandlerInterfaces.Contains(definition))
                {
                    services.TryAddEnumerable(ServiceDescriptor.Scoped(serviceType, type));

                    if (definition == typeof(IIntegrationEventHandler<>))
                    {
                        subscriptions.Add(serviceType.GetGenericArguments()[0]);
                    }
                }
            }
        }
    }

    /// <summary>One shared instance, whether messaging or AddCqrs registers first.</summary>
    internal static IntegrationEventSubscriptions GetOrAddSubscriptions(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(IntegrationEventSubscriptions))?.ImplementationInstance
            is IntegrationEventSubscriptions existing)
        {
            return existing;
        }

        var subscriptions = new IntegrationEventSubscriptions();
        services.AddSingleton(subscriptions);
        return subscriptions;
    }

    private static bool IsConcreteClass(Type type)
        => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false };
}
