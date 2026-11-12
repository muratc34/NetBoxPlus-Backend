using Catalog.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence;

internal sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("Movies");
        builder.MapContentItem("MovieTranslations", "MovieGenres", "MovieCredits");
        builder.ComplexProperty(movie => movie.Video, ContentItemMapping.MapVideo);
    }
}
