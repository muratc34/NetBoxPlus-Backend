using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Media;

namespace Streaming.Infrastructure.Persistence;

internal sealed class VideoConfiguration : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> builder)
    {
        builder.ToTable("Videos");
        builder.HasKey(video => video.Id);
        builder.Property(video => video.Id).ValueGeneratedNever();
        builder.Property(video => video.ContentKind).HasConversion<string>().HasMaxLength(16);
        builder.Property(video => video.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(video => video.FailureReason).HasMaxLength(Video.FailureReasonMaxLength);
        builder.HasIndex(video => video.ContentId);
        builder.HasIndex(video => new { video.Status, video.UploadExpiresAt });
        builder.OwnsMany(video => video.Renditions, rendition =>
        {
            rendition.ToTable("VideoRenditions");
            rendition.WithOwner().HasForeignKey("VideoId");
            rendition.HasKey("VideoId", nameof(Rendition.Height));

            // The height is part of the key and always given: never generated, so a new rendition is inserted.
            rendition.Property(item => item.Height).ValueGeneratedNever();
            rendition.Property(item => item.Playlist).HasMaxLength(64).IsRequired();
        });
        builder.Navigation(video => video.Renditions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TranscodeJobConfiguration : IEntityTypeConfiguration<TranscodeJob>
{
    public void Configure(EntityTypeBuilder<TranscodeJob> builder)
    {
        builder.ToTable("TranscodeJobs");
        builder.HasKey(job => job.VideoId);
        builder.Property(job => job.LastError).HasMaxLength(TranscodeJob.LastErrorMaxLength);
        builder.HasIndex(job => job.AvailableAt);
        builder.HasOne<Video>().WithMany().HasForeignKey(job => job.VideoId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VideoRepository(StreamingDbContext context) : IVideoRepository
{
    public async Task<Video?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => await context.Videos.FindAsync([id], cancellationToken).ConfigureAwait(false);

    public void Add(Video video) => context.Videos.Add(video);
}
