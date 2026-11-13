using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence;
using Streaming.Application.Abstractions;
using Streaming.Domain.Access;
using Streaming.Domain.Playback;
using Streaming.Domain.Videos;

namespace Streaming.Infrastructure.Persistence;

public sealed class StreamingDbContext(DbContextOptions<StreamingDbContext> options) : ServiceDbContext(options), IStreamingReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<Video> Videos => Set<Video>();

    public DbSet<Viewer> Viewers => Set<Viewer>();

    public DbSet<PlaybackSession> PlaybackSessions => Set<PlaybackSession>();

    public DbSet<SubscriptionCopy> Subscriptions => Set<SubscriptionCopy>();

    public DbSet<ProfileCopy> Profiles => Set<ProfileCopy>();

    public DbSet<PlayableTitle> PlayableTitles => Set<PlayableTitle>();

    IQueryable<Video> IStreamingReadModel.Videos => Videos.AsNoTracking();

    IQueryable<PlaybackSession> IStreamingReadModel.PlaybackSessions => PlaybackSessions.AsNoTracking();

    IQueryable<SubscriptionCopy> IStreamingReadModel.Subscriptions => Subscriptions.AsNoTracking();

    IQueryable<ProfileCopy> IStreamingReadModel.Profiles => Profiles.AsNoTracking();

    IQueryable<PlayableTitle> IStreamingReadModel.PlayableTitles => PlayableTitles.AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(StreamingDbContext).Assembly);
}
