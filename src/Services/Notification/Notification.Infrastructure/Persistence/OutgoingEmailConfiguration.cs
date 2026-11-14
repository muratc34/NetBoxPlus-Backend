using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Emails;

namespace Notification.Infrastructure.Persistence;

internal sealed class OutgoingEmailConfiguration : IEntityTypeConfiguration<OutgoingEmail>
{
    /// <summary>Set by the worker that is sending the e-mail; not part of the domain.</summary>
    public const string LeasedUntil = "LeasedUntil";

    public void Configure(EntityTypeBuilder<OutgoingEmail> builder)
    {
        builder.ToTable("OutgoingEmails");
        builder.HasKey(email => email.Id);
        builder.Property(email => email.Id).ValueGeneratedNever();
        builder.Property(email => email.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(email => email.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(email => email.SourceKey).HasMaxLength(OutgoingEmail.SourceKeyMaxLength).IsRequired();
        builder.Property(email => email.Subject).HasMaxLength(OutgoingEmail.SubjectMaxLength).IsRequired();
        builder.Property(email => email.LastError).HasMaxLength(OutgoingEmail.LastErrorMaxLength);
        builder.Property<DateTimeOffset?>(LeasedUntil);

        // The same e-mail for the same thing is queued once, whatever message brought it.
        builder.HasIndex(email => new { email.Kind, email.SourceKey }).IsUnique();
        builder.HasIndex(email => new { email.Status, email.NextAttemptAt });
        builder.HasIndex(email => email.UserId);
    }
}

internal sealed class OutgoingEmailRepository(NotificationDbContext context) : IOutgoingEmailRepository
{
    public async Task<OutgoingEmail?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => await context.Emails.FindAsync([id], cancellationToken).ConfigureAwait(false);

    public Task<bool> ExistsAsync(EmailKind kind, string sourceKey, CancellationToken cancellationToken)
        => context.Emails.AnyAsync(email => email.Kind == kind && email.SourceKey == sourceKey, cancellationToken);

    public void Add(OutgoingEmail email) => context.Emails.Add(email);
}
