using Feed.Application.Abstractions;
using Feed.Domain.Items;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence;

namespace Feed.Infrastructure.Persistence;

public sealed class FeedDbContext(DbContextOptions<FeedDbContext> options)
    : ServiceDbContext(options), IFeedReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<FeedItem> Items => Set<FeedItem>();

    IQueryable<FeedItem> IFeedReadModel.Items => Items.AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(FeedDbContext).Assembly);
}
