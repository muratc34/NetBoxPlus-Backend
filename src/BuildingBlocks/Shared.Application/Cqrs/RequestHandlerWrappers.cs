using Microsoft.Extensions.DependencyInjection;
using Shared.Domain;

namespace Shared.Application.Cqrs;

/// <summary>
/// Bridges a request known only by its runtime type to its strongly typed handler and behaviors.
/// One stateless instance exists per request type, created once by the dispatcher.
/// </summary>
internal abstract class RequestHandlerWrapper<TResponse>
    where TResponse : Result
{
    public abstract Task<TResponse> Handle(object request, IServiceProvider services, CancellationToken cancellationToken);

    protected static THandler GetHandler<THandler>(IServiceProvider services, Type requestType)
        where THandler : notnull
    {
        return services.GetService<THandler>()
            ?? throw new InvalidOperationException(
                $"No handler is registered for '{requestType.FullName}'. Register the handler's assembly with AddCqrs.");
    }

    protected static Task<TResponse> RunPipeline<TRequest>(
        TRequest request,
        IServiceProvider services,
        RequestHandlerDelegate<TResponse> handler,
        CancellationToken cancellationToken)
    {
        var behaviors = services.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();

        var next = handler;
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var inner = next;
            next = ct => behavior.Handle(request, inner, ct);
        }

        return next(cancellationToken);
    }
}

internal sealed class CommandHandlerWrapper<TCommand> : RequestHandlerWrapper<Result>
    where TCommand : ICommand
{
    public override Task<Result> Handle(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var command = (TCommand)request;
        var handler = GetHandler<ICommandHandler<TCommand>>(services, typeof(TCommand));

        return RunPipeline(command, services, ct => handler.Handle(command, ct), cancellationToken);
    }
}

internal sealed class CommandHandlerWrapper<TCommand, TResponse> : RequestHandlerWrapper<Result<TResponse>>
    where TCommand : ICommand<TResponse>
{
    public override Task<Result<TResponse>> Handle(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var command = (TCommand)request;
        var handler = GetHandler<ICommandHandler<TCommand, TResponse>>(services, typeof(TCommand));

        return RunPipeline(command, services, ct => handler.Handle(command, ct), cancellationToken);
    }
}

internal sealed class QueryHandlerWrapper<TQuery, TResponse> : RequestHandlerWrapper<Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
    public override Task<Result<TResponse>> Handle(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var query = (TQuery)request;
        var handler = GetHandler<IQueryHandler<TQuery, TResponse>>(services, typeof(TQuery));

        return RunPipeline(query, services, ct => handler.Handle(query, ct), cancellationToken);
    }
}
