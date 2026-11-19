using Microsoft.EntityFrameworkCore;
using Notification.Application.Abstractions;
using Notification.Domain.Emails;
using Notification.Domain.Recipients;
using Shared.Infrastructure.Persistence;

namespace Notification.Infrastructure.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : ServiceDbContext(options), INotificationReadModel
{
    /// <summary>Configuration key under <c>ConnectionStrings</c>; the same name in every service.</summary>
    public const string ConnectionStringName = "Database";

    public DbSet<Recipient> Recipients => Set<Recipient>();

    public DbSet<OutgoingEmail> Emails => Set<OutgoingEmail>();

    IQueryable<OutgoingEmail> INotificationReadModel.Emails => Emails.AsNoTracking();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
}
