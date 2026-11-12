using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence;
using Streaming.Application.Abstractions;
using Streaming.Domain.Videos;

namespace Streaming.Infrastructure.Persistence;

public sealed class StreamingDbContext(DbContextOptions<StreamingDbContext> options) : ServiceDbContext(options), IStreamingReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<Video> Videos => Set<Video>();

    IQueryable<Video> IStreamingReadModel.Videos => Videos.AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(StreamingDbContext).Assembly);
}
