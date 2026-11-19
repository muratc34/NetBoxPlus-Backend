using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(role => role.Id);

        builder.Property(role => role.Name).HasMaxLength(Role.NameMaxLength).IsRequired();
        builder.Property(role => role.NormalizedName).HasMaxLength(Role.NameMaxLength).IsRequired();
        builder.HasIndex(role => role.NormalizedName).IsUnique();

        // A PostgreSQL text[] column: permissions are only ever read with their role.
        builder.Ignore(role => role.Permissions);
        builder.PrimitiveCollection<List<string>>("_permissions")
            .HasColumnName("Permissions")
            .IsRequired();
    }
}
