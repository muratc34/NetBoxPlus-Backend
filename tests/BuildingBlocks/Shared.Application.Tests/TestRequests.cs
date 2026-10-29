using Shared.Application.Cqrs;
using Shared.Application.Events;
using Shared.Application.Messaging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Shared.Application.Tests;

// Requests and handlers discovered by AddCqrs when it scans this assembly. Every test container
// registers all of them, so each request has exactly one handler.

internal sealed record PingCommand : ICommand;

internal sealed class PingHandler(CallLog log) : ICommandHandler<PingCommand>
{
    public Task<Result> Handle(PingCommand command, CancellationToken cancellationToken)
    {
        log.Add("handler");
        return Task.FromResult(Result.Success());
    }
}

internal sealed record CreateItemCommand(string Name, int Quantity) : ICommand<Guid>;

internal sealed class CreateItemHandler(CallLog log) : ICommandHandler<CreateItemCommand, Guid>
{
    public static readonly Guid CreatedId = Guid.CreateVersion7();

    public Task<Result<Guid>> Handle(CreateItemCommand command, CancellationToken cancellationToken)
    {
        log.Add("handler");
        return Task.FromResult(Result.Success(CreatedId));
    }
}

internal sealed class CreateItemNameValidator : IValidator<CreateItemCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateItemCommand instance)
    {
        if (string.IsNullOrWhiteSpace(instance.Name))
        {
            yield return new ValidationFailure(nameof(instance.Name), "CreateItem.NameRequired", "Name is required.");
        }
    }
}

internal sealed class CreateItemQuantityValidator : IValidator<CreateItemCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateItemCommand instance)
    {
        if (instance.Quantity <= 0)
        {
            yield return new ValidationFailure(nameof(instance.Quantity), "CreateItem.QuantityPositive", "Quantity must be positive.");
        }
    }
}

internal sealed record DeleteItemCommand(Guid Id) : ICommand;

internal sealed class DeleteItemHandler(CallLog log) : ICommandHandler<DeleteItemCommand>
{
    public Task<Result> Handle(DeleteItemCommand command, CancellationToken cancellationToken)
    {
        log.Add("handler");
        return Task.FromResult(Result.Success());
    }
}

internal sealed class DeleteItemValidator : IValidator<DeleteItemCommand>
{
    public IEnumerable<ValidationFailure> Validate(DeleteItemCommand instance)
    {
        if (instance.Id == Guid.Empty)
        {
            yield return new ValidationFailure(nameof(instance.Id), "DeleteItem.IdRequired", "Id is required.");
        }
    }
}

internal sealed record FailingCommand : ICommand;

internal sealed class FailingHandler : ICommandHandler<FailingCommand>
{
    public static readonly Error Conflict = Error.Conflict("Item.Conflict", "The item already exists.");

    public Task<Result> Handle(FailingCommand command, CancellationToken cancellationToken)
        => Task.FromResult(Result.Failure(Conflict));
}

internal sealed record ThrowingCommand : ICommand;

internal sealed class ThrowingHandler : ICommandHandler<ThrowingCommand>
{
    public Task<Result> Handle(ThrowingCommand command, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Boom.");
}

internal sealed record UnhandledCommand : ICommand;

internal sealed record GetItemQuery(int Id) : IQuery<string>;

internal sealed class GetItemHandler(CallLog log) : IQueryHandler<GetItemQuery, string>
{
    public Task<Result<string>> Handle(GetItemQuery query, CancellationToken cancellationToken)
    {
        log.Add("handler");
        return Task.FromResult(Result.Success($"item-{query.Id}"));
    }
}

internal sealed class GetItemValidator : IValidator<GetItemQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetItemQuery instance)
    {
        if (instance.Id <= 0)
        {
            yield return new ValidationFailure(nameof(instance.Id), "GetItem.IdPositive", "Id must be positive.");
        }
    }
}

internal sealed record CancellationAwareQuery : IQuery<bool>;

internal sealed class CancellationAwareHandler : IQueryHandler<CancellationAwareQuery, bool>
{
    public Task<Result<bool>> Handle(CancellationAwareQuery query, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success(cancellationToken.CanBeCanceled));
}

internal sealed record ItemCreated(Guid ItemId) : IDomainEvent;

internal sealed record NobodyListens : IDomainEvent;

internal sealed class FirstItemCreatedHandler(CallLog log) : IDomainEventHandler<ItemCreated>
{
    public Task Handle(ItemCreated domainEvent, CancellationToken cancellationToken)
    {
        log.Add($"first:{domainEvent.ItemId}");
        return Task.CompletedTask;
    }
}

internal sealed class SecondItemCreatedHandler(CallLog log) : IDomainEventHandler<ItemCreated>
{
    public Task Handle(ItemCreated domainEvent, CancellationToken cancellationToken)
    {
        log.Add($"second:{domainEvent.ItemId}");
        return Task.CompletedTask;
    }
}

internal sealed record ItemShipped(Guid ItemId);

internal sealed class ItemShippedHandler : IIntegrationEventHandler<ItemShipped>
{
    public Task<Result> Handle(ItemShipped integrationEvent, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success());
}

/// <summary>
/// A second handler for <see cref="PingCommand"/>, used only to test duplicate detection. It is
/// generic so that assembly scanning, which skips open generic types, never picks it up.
/// </summary>
internal sealed class DuplicatePingHandler<TUnused> : ICommandHandler<PingCommand>
{
    public Task<Result> Handle(PingCommand command, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success());
}

/// <summary>Records entry and exit around the rest of the pipeline.</summary>
internal sealed class RecordingBehavior<TRequest, TResponse>(CallLog log, string name)
    : IPipelineBehavior<TRequest, TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Add($"{name}:before");
        var result = await next(cancellationToken);
        log.Add($"{name}:after");
        return result;
    }
}

internal sealed class OuterBehavior<TRequest, TResponse>(CallLog log)
    : IPipelineBehavior<TRequest, TResponse>
    where TResponse : Result
{
    private readonly RecordingBehavior<TRequest, TResponse> _inner = new(log, "outer");

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => _inner.Handle(request, next, cancellationToken);
}

internal sealed class InnerBehavior<TRequest, TResponse>(CallLog log)
    : IPipelineBehavior<TRequest, TResponse>
    where TResponse : Result
{
    private readonly RecordingBehavior<TRequest, TResponse> _inner = new(log, "inner");

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => _inner.Handle(request, next, cancellationToken);
}
