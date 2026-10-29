using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Infrastructure.Persistence.Inbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace Shared.Infrastructure.Persistence;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Type).HasMaxLength(500).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.TraceParent).HasMaxLength(128);
        builder.Property(message => message.LastError).HasMaxLength(2000);

        // The processor only ever looks for unprocessed messages, oldest first.
        builder.HasIndex(message => message.OccurredAt)
            .HasFilter($"\"{nameof(OutboxMessage.ProcessedAt)}\" IS NULL");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public const string PrimaryKeyName = "PK_InboxMessages";

    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");
        builder.HasKey(message => new { message.MessageId, message.Consumer }).HasName(PrimaryKeyName);

        builder.Property(message => message.Consumer).HasMaxLength(500);
    }
}
