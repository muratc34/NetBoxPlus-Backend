namespace Shared.Application;

/// <summary>
/// Commits the changes of the current scope. Called by the transaction behavior after a command
/// succeeds; application code does not call it directly.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
