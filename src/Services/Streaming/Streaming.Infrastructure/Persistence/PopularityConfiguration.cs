using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Streaming.Domain.Popularity;

namespace Streaming.Infrastructure.Persistence;

internal sealed class PopularityRankingConfiguration : IEntityTypeConfiguration<PopularityRanking>
{
    public void Configure(EntityTypeBuilder<PopularityRanking> builder)
    {
        builder.ToTable("PopularityRankings");
        builder.HasKey(ranking => ranking.Id);
        builder.Property(ranking => ranking.Id).ValueGeneratedNever();
        builder.Property(ranking => ranking.Version).IsConcurrencyToken();
        builder.HasMany(ranking => ranking.Titles).WithOne().HasForeignKey(title => title.RankingId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(ranking => ranking.Titles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PopularTitleConfiguration : IEntityTypeConfiguration<PopularTitle>
{
    public void Configure(EntityTypeBuilder<PopularTitle> builder)
    {
        builder.ToTable("PopularTitles");
        builder.HasKey(title => new { title.RankingId, title.TitleId });
    }
}

internal sealed class DailyPlaysConfiguration : IEntityTypeConfiguration<DailyPlays>
{
    public void Configure(EntityTypeBuilder<DailyPlays> builder)
    {
        builder.ToTable("DailyPlays");
        builder.HasKey(plays => new { plays.Day, plays.TitleId });
    }
}

internal sealed class PopularityRepository(StreamingDbContext context) : IPopularityRepository
{
    public Task<PopularityRanking?> GetAsync(CancellationToken cancellationToken)
        => context.PopularityRankings.Include(ranking => ranking.Titles).FirstOrDefaultAsync(ranking => ranking.Id == PopularityRanking.SingletonId, cancellationToken);

    public void Add(PopularityRanking ranking) => context.PopularityRankings.Add(ranking);

    public void AddDailyPlays(IEnumerable<DailyPlays> plays) => context.DailyPlays.AddRange(plays);

    public async Task RemoveDailyPlaysBeforeAsync(DateOnly day, CancellationToken cancellationToken)
        => context.DailyPlays.RemoveRange(await context.DailyPlays.Where(plays => plays.Day < day).ToListAsync(cancellationToken).ConfigureAwait(false));
}
