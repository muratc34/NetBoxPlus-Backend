using System.Collections.Concurrent;
using Shared.Domain;

namespace Shared.Application.Cqrs;

/// <summary>
/// Resolves handlers from the current scope. Reflection is used once per request type to build a
/// wrapper; every later call for that type reuses the cached wrapper.
/// </summary>
internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapper<Result>> Commands = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> CommandsWithResponse = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> Queries = new();

    public Task<Result> Send(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = Commands.GetOrAdd(
            command.GetType(),
            static type => Create<RequestHandlerWrapper<Result>>(typeof(CommandHandlerWrapper<>), type));

        return wrapper.Handle(command, services, cancellationToken);
    }

    public Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (RequestHandlerWrapper<Result<TResponse>>)CommandsWithResponse.GetOrAdd(
            (command.GetType(), typeof(TResponse)),
            static key => Create<RequestHandlerWrapper<Result<TResponse>>>(typeof(CommandHandlerWrapper<,>), key.Request, key.Response));

        return wrapper.Handle(command, services, cancellationToken);
    }

    public Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = (RequestHandlerWrapper<Result<TResponse>>)Queries.GetOrAdd(
            (query.GetType(), typeof(TResponse)),
            static key => Create<RequestHandlerWrapper<Result<TResponse>>>(typeof(QueryHandlerWrapper<,>), key.Request, key.Response));

        return wrapper.Handle(query, services, cancellationToken);
    }

    private static TWrapper Create<TWrapper>(Type openWrapperType, params Type[] typeArguments)
        => (TWrapper)Activator.CreateInstance(openWrapperType.MakeGenericType(typeArguments))!;
}
