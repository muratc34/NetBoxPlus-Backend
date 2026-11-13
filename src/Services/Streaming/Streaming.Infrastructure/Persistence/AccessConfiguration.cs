using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Streaming.Domain.Access;

namespace Streaming.Infrastructure.Persistence;

// The revision is the concurrency token: two messages applied at once cannot both win, the loser is
// retried and then compares against the winner's revision.

internal sealed class SubscriptionCopyConfiguration : IEntityTypeConfiguration<SubscriptionCopy>
{
    public void Configure(EntityTypeBuilder<SubscriptionCopy> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();
        builder.Property(subscription => subscription.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(subscription => subscription.Quality).HasConversion<string>().HasMaxLength(8);
        builder.Property(subscription => subscription.Revision).IsConcurrencyToken();
        builder.HasIndex(subscription => subscription.UserId);
    }
}

internal sealed class ProfileCopyConfiguration : IEntityTypeConfiguration<ProfileCopy>
{
    public void Configure(EntityTypeBuilder<ProfileCopy> builder)
    {
        builder.ToTable("Profiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.Property(profile => profile.MaxAgeRating).HasConversion<string>().HasMaxLength(8);
        builder.Property(profile => profile.Revision).IsConcurrencyToken();
        builder.HasIndex(profile => profile.UserId);
    }
}

internal sealed class PlayableTitleConfiguration : IEntityTypeConfiguration<PlayableTitle>
{
    public void Configure(EntityTypeBuilder<PlayableTitle> builder)
    {
        builder.ToTable("PlayableTitles");
        builder.HasKey(title => title.Id);
        builder.Property(title => title.Id).ValueGeneratedNever();
        builder.Property(title => title.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(title => title.AgeRating).HasConversion<string>().HasMaxLength(8);
        builder.Property(title => title.Revision).IsConcurrencyToken();
        builder.OwnsMany(title => title.Items, item =>
        {
            item.ToTable("PlayableItems");
            item.WithOwner().HasForeignKey("TitleId");

            // A movie or an episode belongs to one title; playback looks it up by itself.
            item.HasKey(nameof(PlayableItem.ContentId));
            item.Property(entry => entry.ContentId).ValueGeneratedNever();
            item.HasIndex("TitleId");
        });
        builder.Navigation(title => title.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SubscriptionCopyRepository(StreamingDbContext context) : ISubscriptionCopyRepository
{
    public async Task<SubscriptionCopy?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken)
        => await context.Subscriptions.FindAsync([subscriptionId], cancellationToken).ConfigureAwait(false);

    public void Add(SubscriptionCopy subscription) => context.Subscriptions.Add(subscription);
}

internal sealed class ProfileCopyRepository(StreamingDbContext context) : IProfileCopyRepository
{
    public async Task<ProfileCopy?> GetByIdAsync(Guid profileId, CancellationToken cancellationToken)
        => await context.Profiles.FindAsync([profileId], cancellationToken).ConfigureAwait(false);

    public void Add(ProfileCopy profile) => context.Profiles.Add(profile);
}

internal sealed class PlayableTitleRepository(StreamingDbContext context) : IPlayableTitleRepository
{
    public async Task<PlayableTitle?> GetByIdAsync(Guid titleId, CancellationToken cancellationToken)
        => await context.PlayableTitles.FindAsync([titleId], cancellationToken).ConfigureAwait(false);

    public void Add(PlayableTitle title) => context.PlayableTitles.Add(title);
}
