using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Shared.Domain;
using Shared.Domain.Abstractions;

namespace Shared.UnitOfWork;


public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
public class UnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IMediator _mediator;

    public UnitOfWork(TContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<int>  SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditableEntities();
        UpdateSoftDeletableEntities();

        var result = await _context.SaveChangesAsync(cancellationToken);

        await PublishDomainEventsAsync(cancellationToken);

        return result;
    }

    private void UpdateAuditableEntities()
    {
        IEnumerable<EntityEntry<IAuditableEntity>> entries =
            _context
                .ChangeTracker
                .Entries<IAuditableEntity>();

        foreach (EntityEntry<IAuditableEntity> entityEntry in entries)
        {
            if (entityEntry.State == EntityState.Added)
            {
                entityEntry.Property(a => a.CreatedOnUtc)
                    .CurrentValue = DateTime.UtcNow;
            }

            if(entityEntry.State == EntityState.Modified)
            {
                entityEntry.Property(a => a.ModifiedOnUtc)
                    .CurrentValue = DateTime.UtcNow;
            }
        }
    }

    private void UpdateSoftDeletableEntities()
    {
        IEnumerable<EntityEntry<ISoftDeletableEntity>> entries =
            _context
                .ChangeTracker
                .Entries<ISoftDeletableEntity>();

        foreach(EntityEntry<ISoftDeletableEntity> entityEntry in entries)
        {
            if (entityEntry.State == EntityState.Deleted)
            {
                entityEntry.Property(a => a.DeletedOnUtc)
                    .CurrentValue = DateTime.UtcNow;

                entityEntry.Property(a => a.Deleted)
                    .CurrentValue = true;
            }
        }
    }

    private async Task PublishDomainEventsAsync(CancellationToken cancellationToken)
    {
        var domainEvents = _context.ChangeTracker
            .Entries<Entity>()
            .Select(a => a.Entity)
            .SelectMany(entity =>
            {
                var domainEvents = entity.GetDomainEvents();
                entity.ClearDomainEvents();
                return domainEvents;
            }).ToList();

        // Domain events publish
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent);
        }
    }
}
