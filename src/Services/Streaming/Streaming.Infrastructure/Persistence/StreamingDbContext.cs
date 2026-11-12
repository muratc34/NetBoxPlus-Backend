using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence;

namespace Streaming.Infrastructure.Persistence;

public sealed class StreamingDbContext(DbContextOptions<StreamingDbContext> options) : ServiceDbContext(options)
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(StreamingDbContext).Assembly);
}
