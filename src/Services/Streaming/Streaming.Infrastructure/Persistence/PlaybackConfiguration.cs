using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Streaming.Domain.Playback;

namespace Streaming.Infrastructure.Persistence;

internal sealed class ViewerConfiguration : IEntityTypeConfiguration<Viewer>
{
    public void Configure(EntityTypeBuilder<Viewer> builder)
    {
        builder.ToTable("Viewers");
        builder.HasKey(viewer => viewer.Id);
        builder.Property(viewer => viewer.Id).ValueGeneratedNever();
        builder.Property(viewer => viewer.Version).IsConcurrencyToken();
        builder.HasMany(viewer => viewer.Sessions).WithOne().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(viewer => viewer.Sessions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PlaybackSessionConfiguration : IEntityTypeConfiguration<PlaybackSession>
{
    public void Configure(EntityTypeBuilder<PlaybackSession> builder)
    {
        builder.ToTable("PlaybackSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.HasIndex(session => new { session.UserId, session.LastHeartbeatAt });
        builder.HasIndex(session => session.LastHeartbeatAt);
    }
}

internal sealed class ViewerRepository(StreamingDbContext context, TimeProvider timeProvider) : IViewerRepository
{
    // Only sessions that may still be live: the screen count needs nothing else.
    public Task<Viewer?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var since = timeProvider.GetUtcNow() - PlaybackSession.Timeout;
        return context.Viewers
            .Include(viewer => viewer.Sessions.Where(session => session.EndedAt == null && session.LastHeartbeatAt > since))
            .FirstOrDefaultAsync(viewer => viewer.Id == userId, cancellationToken);
    }

    public void Add(Viewer viewer) => context.Viewers.Add(viewer);
}

internal sealed class PlaybackSessionRepository(StreamingDbContext context) : IPlaybackSessionRepository
{
    public async Task<bool> TryHeartbeatAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now - PlaybackSession.Timeout;
        var updated = await context.PlaybackSessions
            .Where(session => session.Id == sessionId && session.UserId == userId && session.EndedAt == null && session.LastHeartbeatAt > since)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.LastHeartbeatAt, now), cancellationToken)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> EndAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var mine = context.PlaybackSessions.Where(session => session.Id == sessionId && session.UserId == userId);
        await mine.Where(session => session.EndedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.EndedAt, now), cancellationToken)
            .ConfigureAwait(false);
        return await mine.AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}
