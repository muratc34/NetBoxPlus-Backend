using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sample.Domain.Notes;

namespace Sample.Infrastructure.Persistence;

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes");
        builder.HasKey(note => note.Id);

        // A single-value object stored as a column of the same table.
        builder.ComplexProperty(note => note.Title, title => title.Property(t => t.Value)
            .HasColumnName("Title")
            .HasMaxLength(NoteTitle.MaxLength)
            .IsRequired());

        builder.Property(note => note.Content).HasMaxLength(Note.ContentMaxLength).IsRequired();

        builder.HasQueryFilter(note => !note.IsDeleted);
    }
}
