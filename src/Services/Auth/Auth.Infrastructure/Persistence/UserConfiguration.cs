using Auth.Domain.Roles;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infrastructure.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);

        // A value conversion rather than a complex property: the address is looked up and must be
        // unique, which needs an index on the column. Stored values are already valid.
        builder.Property(user => user.Email)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value).Value)
            .HasMaxLength(EmailAddress.MaxLength)
            .IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.Property(user => user.EmailRevision).HasDefaultValue(1).ValueGeneratedNever();

        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();

        // A deleted account stays for the audit trail but is gone for everything else.
        builder.HasQueryFilter(user => user.DeletedAt == null);

        builder.HasMany(user => user.Roles)
            .WithOne()
            .HasForeignKey(role => role.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(user => user.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(user => user.Profiles)
            .WithOne()
            .HasForeignKey(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(user => user.Profiles).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.ToTable("Profiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.Property(profile => profile.Name).HasMaxLength(Profile.NameMaxLength).IsRequired();
        builder.Property(profile => profile.NormalizedName).HasMaxLength(Profile.NameMaxLength).IsRequired();
        builder.Property(profile => profile.MaxAgeRating).HasConversion<string>().HasMaxLength(8);
        builder.HasIndex(profile => new { profile.UserId, profile.NormalizedName }).IsUnique();
        builder.Property(profile => profile.PinHash).HasMaxLength(512);
    }
}

internal sealed class ProfileUnlockConfiguration : IEntityTypeConfiguration<ProfileUnlock>
{
    public void Configure(EntityTypeBuilder<ProfileUnlock> builder)
    {
        builder.ToTable("ProfileUnlocks");
        builder.HasKey(unlock => new { unlock.SessionId, unlock.ProfileId });
        builder.HasIndex(unlock => unlock.ProfileId);

        // A removed profile (or account) takes its unlocks with it.
        builder.HasOne<Profile>().WithMany().HasForeignKey(unlock => unlock.ProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProfileUnlockRepository(AuthDbContext context) : IProfileUnlockRepository
{
    public Task<bool> ExistsAsync(Guid sessionId, Guid profileId, CancellationToken cancellationToken)
        => context.ProfileUnlocks.AnyAsync(unlock => unlock.SessionId == sessionId && unlock.ProfileId == profileId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ProfileIdsAsync(Guid sessionId, CancellationToken cancellationToken)
        => await context.ProfileUnlocks.AsNoTracking()
            .Where(unlock => unlock.SessionId == sessionId)
            .OrderBy(unlock => unlock.ProfileId)
            .Select(unlock => unlock.ProfileId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(ProfileUnlock unlock) => context.ProfileUnlocks.Add(unlock);

    public async Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken)
        => context.ProfileUnlocks.RemoveRange(await context.ProfileUnlocks.Where(unlock => unlock.ProfileId == profileId).ToListAsync(cancellationToken).ConfigureAwait(false));
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(userRole => new { userRole.UserId, userRole.RoleId });

        // Deleting a role takes it away from every user who had it.
        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(userRole => userRole.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
