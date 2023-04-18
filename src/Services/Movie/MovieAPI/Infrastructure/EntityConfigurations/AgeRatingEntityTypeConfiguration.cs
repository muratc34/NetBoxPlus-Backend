using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MovieAPI.Model;

namespace MovieAPI.Infrastructure.EntityConfigurations
{
    public class AgeRatingEntityTypeConfiguration
        : IEntityTypeConfiguration<AgeRating>
    {
        public void Configure(EntityTypeBuilder<AgeRating> builder)
        {
            builder.ToTable("AgeRating");

            builder.HasKey(a => a.Id);

            builder.Property(a => a.Rating)
                .IsRequired();
        }
    }
}
