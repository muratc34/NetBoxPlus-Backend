using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MovieAPI.Model;

namespace MovieAPI.Infrastructure.EntityConfigurations
{
    public class MovieEntityTypeConfiguration
        : IEntityTypeConfiguration<Movie>
    {
        public void Configure(EntityTypeBuilder<Movie> builder)
        {
            builder.ToTable("Movies");

            builder.HasKey(mi => mi.Id);

            builder.Property(mi => mi.Title)
                .IsRequired();

            builder.Property(mi => mi.MovieClickCount)
                .IsRequired();

            builder.Property(mi => mi.ReleaseYear)
                .IsRequired()
                .HasMaxLength(4);

            builder.Property(mi => mi.MpaaRating)
                .IsRequired()
                .HasConversion(
                    v => v.ToString(),
                    v => (MpaaRatingType)Enum.Parse(typeof(MpaaRatingType), v));

        }
    }
}
