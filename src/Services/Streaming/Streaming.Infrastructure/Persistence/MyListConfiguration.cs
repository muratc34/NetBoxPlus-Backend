using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Streaming.Domain.Lists;

namespace Streaming.Infrastructure.Persistence;

internal sealed class MyListConfiguration : IEntityTypeConfiguration<MyList>
{
    public void Configure(EntityTypeBuilder<MyList> builder)
    {
        builder.ToTable("MyLists");
        builder.HasKey(list => list.Id);
        builder.Property(list => list.Id).ValueGeneratedNever();
        builder.Property(list => list.Version).IsConcurrencyToken();
        builder.HasIndex(list => list.UserId);
        builder.HasMany(list => list.Entries).WithOne().HasForeignKey(entry => entry.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(list => list.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MyListEntryConfiguration : IEntityTypeConfiguration<MyListEntry>
{
    public void Configure(EntityTypeBuilder<MyListEntry> builder)
    {
        builder.ToTable("MyListEntries");
        builder.HasKey(entry => new { entry.ProfileId, entry.TitleId });
        builder.HasIndex(entry => new { entry.ProfileId, entry.AddedAt });
    }
}

internal sealed class MyListRepository(StreamingDbContext context) : IMyListRepository
{
    public Task<MyList?> GetAsync(Guid profileId, CancellationToken cancellationToken)
        => context.MyLists.Include(list => list.Entries).FirstOrDefaultAsync(list => list.Id == profileId, cancellationToken);

    public void Add(MyList list) => context.MyLists.Add(list);

    public async Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken)
        => context.MyLists.RemoveRange(await context.MyLists.Include(list => list.Entries).Where(list => list.Id == profileId).ToListAsync(cancellationToken).ConfigureAwait(false));

    public async Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken)
        => context.MyLists.RemoveRange(await context.MyLists.Include(list => list.Entries).Where(list => list.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false));
}
