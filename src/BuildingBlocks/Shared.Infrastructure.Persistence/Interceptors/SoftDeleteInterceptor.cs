using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.Domain;

namespace Shared.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Turns the deletion of an <see cref="ISoftDeletableEntity"/> into an update that sets
/// <see cref="ISoftDeletableEntity.IsDeleted"/> and <see cref="ISoftDeletableEntity.DeletedAt"/>.
/// Owned values of such an entity are kept, since the row stays.
/// </summary>
internal sealed class SoftDeleteInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletableEntity>().Where(e => e.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;
            entry.Property(nameof(ISoftDeletableEntity.IsDeleted)).CurrentValue = true;
            entry.Property(nameof(ISoftDeletableEntity.DeletedAt)).CurrentValue = now;

            KeepOwnedValues(entry);
        }
    }

    // Deleting an owner cascades to its owned entries; with the owner kept, they must be kept too.
    private static void KeepOwnedValues(EntityEntry entry)
    {
        foreach (var navigation in entry.Navigations.Where(n => n.Metadata.TargetEntityType.IsOwned()))
        {
            var owned = navigation is CollectionEntry collection
                ? collection.CurrentValue?.Cast<object>() ?? []
                : navigation.CurrentValue is { } single ? [single] : [];

            foreach (var ownedEntry in owned.Select(value => entry.Context.Entry(value)))
            {
                if (ownedEntry.State == EntityState.Deleted)
                {
                    ownedEntry.State = EntityState.Unchanged;
                    KeepOwnedValues(ownedEntry);
                }
            }
        }
    }
}
