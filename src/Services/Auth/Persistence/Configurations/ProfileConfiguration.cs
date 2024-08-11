using Auth.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Persistence.Configurations;

public sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.HasKey(profile => profile.Id);

        builder.OwnsOne(profile => profile.ProfileName, profileNameBuilder =>
        {
            profileNameBuilder.WithOwner();

            profileNameBuilder.Property(profileName => profileName.Value)
                .HasColumnName(nameof(Profile.ProfileName))
                .HasMaxLength(ProfileName.MaxLength)
                .IsRequired();
        });

        builder.Property<byte[]?>(profile => profile.PinHash);

        builder.Property<byte[]?>(profile => profile.PinSalt);

        builder.Property(profile => profile.UserId).IsRequired();

        builder.Property(profile => profile.CreatedOnUtc).IsRequired();

        builder.Property(profile => profile.ModifiedOnUtc);

        builder.Property(profile => profile.DeletedOnUtc);

        builder.Property(profile => profile.Deleted).HasDefaultValue(false);

        builder.HasQueryFilter(profile => !profile.Deleted);
    }
}
