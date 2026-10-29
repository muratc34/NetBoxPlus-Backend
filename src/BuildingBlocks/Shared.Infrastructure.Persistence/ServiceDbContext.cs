using Microsoft.EntityFrameworkCore;
using Shared.Domain;
using Shared.Infrastructure.Persistence.Inbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace Shared.Infrastructure.Persistence;

/// <summary>
/// Base of every service's DbContext. Adds the outbox and inbox tables and keeps domain events out
/// of the model. Services put their own mapping in <see cref="ConfigureModel"/>.
/// </summary>
public abstract class ServiceDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>The service's own model: entity configurations, schema, conventions.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    // Sealed so the shared configuration always runs, and runs after the service's own: domain
    // events can only be ignored on entity types the service has already added.
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(type => typeof(AggregateRoot).IsAssignableFrom(type.ClrType))
                     .ToList())
        {
            modelBuilder.Entity(entityType.ClrType).Ignore(nameof(AggregateRoot.DomainEvents));
        }
    }
}
