using Shared.Application;

namespace Shared.Infrastructure.Persistence;

internal sealed class EfUnitOfWork<TContext>(TContext context) : IUnitOfWork
    where TContext : ServiceDbContext
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);
}
