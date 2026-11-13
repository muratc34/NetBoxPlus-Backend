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

internal sealed class WatchProgressConfiguration : IEntityTypeConfiguration<WatchProgress>
{
    public void Configure(EntityTypeBuilder<WatchProgress> builder)
    {
        builder.ToTable("WatchProgress");
        builder.HasKey(progress => new { progress.ProfileId, progress.ContentId });
        builder.HasIndex(progress => new { progress.ProfileId, progress.UpdatedAt });
        builder.HasIndex(progress => new { progress.ProfileId, progress.TitleId });
    }
}

internal sealed class WatchProgressRepository(StreamingDbContext context) : IWatchProgressRepository
{
    public Task SaveAsync(WatchProgress progress, CancellationToken cancellationToken)
        => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "WatchProgress" ("ProfileId", "ContentId", "UserId", "TitleId", "PositionSeconds", "DurationSeconds", "Completed", "UpdatedAt")
            VALUES ({progress.ProfileId}, {progress.ContentId}, {progress.UserId}, {progress.TitleId}, {progress.PositionSeconds}, {progress.DurationSeconds}, {progress.Completed}, {progress.UpdatedAt})
            ON CONFLICT ("ProfileId", "ContentId") DO UPDATE SET
                "TitleId" = EXCLUDED."TitleId",
                "PositionSeconds" = EXCLUDED."PositionSeconds",
                "DurationSeconds" = EXCLUDED."DurationSeconds",
                "Completed" = EXCLUDED."Completed",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """,
            cancellationToken);

    public Task<WatchProgress?> FindAsync(Guid profileId, Guid contentId, CancellationToken cancellationToken)
        => context.WatchProgress.AsNoTracking().FirstOrDefaultAsync(progress => progress.ProfileId == profileId && progress.ContentId == contentId, cancellationToken);

    public async Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken)
        => context.WatchProgress.RemoveRange(await context.WatchProgress.Where(progress => progress.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false));
}
