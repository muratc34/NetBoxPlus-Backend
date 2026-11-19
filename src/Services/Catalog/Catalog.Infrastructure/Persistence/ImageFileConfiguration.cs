using Catalog.Domain.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence;

internal sealed class ImageFileConfiguration : IEntityTypeConfiguration<ImageFile>
{
    public const int NameMaxLength = 64;

    public void Configure(EntityTypeBuilder<ImageFile> builder)
    {
        builder.ToTable("ImageFiles");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).ValueGeneratedNever();
        builder.Property(file => file.Name).HasMaxLength(NameMaxLength);

        // A token: of two requests attaching the same upload, only one can move it on from Pending.
        builder.Property(file => file.Status).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();

        builder.HasIndex(file => file.Name).IsUnique();

        // The clean-up looks for finished uploads and retired images.
        builder.HasIndex(file => new { file.Status, file.ExpiresAt });
    }
}

internal sealed class ImageFileRepository(CatalogDbContext context) : IImageFileRepository
{
    public Task<ImageFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Set<ImageFile>().FirstOrDefaultAsync(file => file.Id == id, cancellationToken);

    public Task<ImageFile?> GetByNameAsync(string name, CancellationToken cancellationToken)
        => context.Set<ImageFile>().FirstOrDefaultAsync(file => file.Name == name, cancellationToken);

    public void Add(ImageFile file) => context.Set<ImageFile>().Add(file);
}
