using Microsoft.EntityFrameworkCore;
using Sample.Application.Abstractions;
using Sample.Domain.Notes;
using Shared.Infrastructure.Persistence;

namespace Sample.Infrastructure.Persistence;

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options)
    : ServiceDbContext(options), ISampleReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<Note> Notes => Set<Note>();

    IQueryable<Note> ISampleReadModel.Notes => Notes.AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(SampleDbContext).Assembly);
}
