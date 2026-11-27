using Microsoft.EntityFrameworkCore;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Notification.Domain.Recipients;
using Notification.Domain.SignIns;
using Shared.Infrastructure.Persistence;

namespace Notification.Infrastructure.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : ServiceDbContext(options), INotificationReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<Recipient> Recipients => Set<Recipient>();

    public DbSet<OutgoingEmail> Emails => Set<OutgoingEmail>();

    public DbSet<KnownDevice> KnownDevices => Set<KnownDevice>();

    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();

    public DbSet<UnsubscribeLink> UnsubscribeLinks => Set<UnsubscribeLink>();

    IQueryable<OutgoingEmail> INotificationReadModel.Emails => Emails.AsNoTracking();

    IQueryable<NotificationPreference> INotificationReadModel.Preferences => Preferences.AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
}
