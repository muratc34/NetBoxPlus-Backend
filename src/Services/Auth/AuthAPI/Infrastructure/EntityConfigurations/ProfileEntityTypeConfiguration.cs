using AuthAPI.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthAPI.Infrastructure.EntityConfigurations
{
    public class ProfileEntityTypeConfiguration
        : IEntityTypeConfiguration<Profile>
    {
        public void Configure(EntityTypeBuilder<Profile> builder)
        {
            builder.ToTable("Profiles");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.ProfileName)
                .IsRequired();

            builder.Property(p => p.PinSalt)
                .IsRequired();

            builder.Property(p => p.PinHash)
                .IsRequired();
        }
    }
}
