using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Domain.Recipients;

namespace Notification.Infrastructure.Persistence;

internal sealed class RecipientConfiguration : IEntityTypeConfiguration<Recipient>
{
    public void Configure(EntityTypeBuilder<Recipient> builder)
    {
        builder.ToTable("Recipients");
        builder.HasKey(recipient => recipient.Id);
        builder.Property(recipient => recipient.Id).ValueGeneratedNever();
        builder.Property(recipient => recipient.Email).HasMaxLength(Recipient.EmailMaxLength).IsRequired();
    }
}

internal sealed class RecipientRepository(NotificationDbContext context) : IRecipientRepository
{
    public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken)
        => context.Recipients.AnyAsync(recipient => recipient.Id == userId, cancellationToken);

    public void Add(Recipient recipient) => context.Recipients.Add(recipient);
}
