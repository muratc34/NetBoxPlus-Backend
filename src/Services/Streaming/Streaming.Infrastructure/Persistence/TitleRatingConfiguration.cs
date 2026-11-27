using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Streaming.Domain.Ratings;

namespace Streaming.Infrastructure.Persistence;

internal sealed class TitleRatingConfiguration : IEntityTypeConfiguration<TitleRating>
{
    public void Configure(EntityTypeBuilder<TitleRating> builder)
    {
        builder.ToTable("TitleRatings");
        builder.HasKey(rating => new { rating.ProfileId, rating.TitleId });
        builder.Property(rating => rating.Level).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(rating => new { rating.ProfileId, rating.RatedAt });
        builder.HasIndex(rating => rating.UserId);
    }
}

internal sealed class TitleRatingRepository(StreamingDbContext context) : ITitleRatingRepository
{
    public Task<TitleRating?> FindAsync(Guid profileId, Guid titleId, CancellationToken cancellationToken)
        => context.TitleRatings.FirstOrDefaultAsync(rating => rating.ProfileId == profileId && rating.TitleId == titleId, cancellationToken);

    public void Add(TitleRating rating) => context.TitleRatings.Add(rating);

    public void Remove(TitleRating rating) => context.TitleRatings.Remove(rating);

    public async Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken)
        => context.TitleRatings.RemoveRange(await context.TitleRatings.Where(rating => rating.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false));

    public async Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken)
        => context.TitleRatings.RemoveRange(await context.TitleRatings.Where(rating => rating.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false));
}
