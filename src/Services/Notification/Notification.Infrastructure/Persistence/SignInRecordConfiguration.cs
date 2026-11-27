using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.SignIns;

namespace Notification.Infrastructure.Persistence;

internal sealed class KnownDeviceConfiguration : IEntityTypeConfiguration<KnownDevice>
{
    public void Configure(EntityTypeBuilder<KnownDevice> builder)
    {
        builder.ToTable("KnownDevices");
        builder.HasKey(device => new { device.UserId, device.Label });
        builder.Property(device => device.Label).HasMaxLength(KnownDevice.LabelMaxLength);
        builder.HasIndex(device => device.LastSeenAt);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(preference => preference.UserId);
        builder.Property(preference => preference.UserId).ValueGeneratedNever();
    }
}

internal sealed class UnsubscribeLinkConfiguration : IEntityTypeConfiguration<UnsubscribeLink>
{
    public void Configure(EntityTypeBuilder<UnsubscribeLink> builder)
    {
        builder.ToTable("UnsubscribeLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.Property(link => link.TokenHash).HasMaxLength(32).IsRequired();
        builder.HasIndex(link => link.TokenHash).IsUnique();
        builder.HasIndex(link => link.UserId);
        builder.HasIndex(link => link.ExpiresAt);
    }
}

internal sealed class SignInRecordRepository(NotificationDbContext context) : ISignInRecordRepository
{
    public Task<KnownDevice?> GetDeviceAsync(Guid userId, string label, CancellationToken cancellationToken)
        => context.KnownDevices.FirstOrDefaultAsync(device => device.UserId == userId && device.Label == label, cancellationToken);

    public void AddDevice(KnownDevice device) => context.KnownDevices.Add(device);

    public Task<NotificationPreference?> GetPreferenceAsync(Guid userId, CancellationToken cancellationToken)
        => context.Preferences.FirstOrDefaultAsync(preference => preference.UserId == userId, cancellationToken);

    public void AddPreference(NotificationPreference preference) => context.Preferences.Add(preference);

    public Task<UnsubscribeLink?> GetLinkAsync(byte[] tokenHash, CancellationToken cancellationToken)
        => context.UnsubscribeLinks.FirstOrDefaultAsync(link => link.TokenHash == tokenHash, cancellationToken);

    public void AddLink(UnsubscribeLink link) => context.UnsubscribeLinks.Add(link);

    public async Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        context.KnownDevices.RemoveRange(await context.KnownDevices.Where(device => device.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false));
        context.Preferences.RemoveRange(await context.Preferences.Where(preference => preference.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false));
        context.UnsubscribeLinks.RemoveRange(await context.UnsubscribeLinks.Where(link => link.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false));
    }
}
