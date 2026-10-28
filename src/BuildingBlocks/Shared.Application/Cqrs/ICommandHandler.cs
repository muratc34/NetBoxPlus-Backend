using Shared.Domain;

namespace Shared.Application.Cqrs;

/// <summary>
/// Handles a command. Handlers do not call <see cref="IUnitOfWork.SaveChangesAsync"/>; the
/// transaction behavior saves when the handler succeeds.
/// </summary>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ICommandHandler{TCommand}"/>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}
