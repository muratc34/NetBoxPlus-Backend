using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MovieAPI.Model;

namespace MovieAPI.Infrastructure.EntityConfigurations
{
    public class GenreEntityTypeConfiguration
        : IEntityTypeConfiguration<Genre>
    {
        public void Configure(EntityTypeBuilder<Genre> builder)
        {
            builder.ToTable("Genres");

            builder.HasKey(g => g.Id);

            builder.Property(g => g.GenreCode)
                .IsRequired();

            builder.Property(g => g.GenreTitle)
            .IsRequired();
        }
    }
}
