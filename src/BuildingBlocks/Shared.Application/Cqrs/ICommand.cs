namespace Shared.Application.Cqrs;

/// <summary>
/// Common marker of every command. Behaviors that apply only to commands, such as the transaction
/// behavior, constrain on this type.
/// </summary>
public interface ICommandBase;

/// <summary>A request that changes state and returns <see cref="Shared.Domain.Result"/>.</summary>
public interface ICommand : ICommandBase;

/// <summary>A request that changes state and returns <see cref="Shared.Domain.Result{TValue}"/>.</summary>
public interface ICommand<TResponse> : ICommandBase;
