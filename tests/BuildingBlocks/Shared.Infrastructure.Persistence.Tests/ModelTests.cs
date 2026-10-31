using Microsoft.EntityFrameworkCore;
using Shared.Domain;
using Shared.Infrastructure.Persistence.Auditing;
using Shared.Infrastructure.Persistence.Inbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace Shared.Infrastructure.Persistence.Tests;

/// <summary>Checks the EF model only — no database needed.</summary>
public sealed class ModelTests
{
    [Fact]
    public void DomainEvents_AreNotPartOfTheModel()
    {
        using var context = CreateContext();
        var product = context.Model.FindEntityType(typeof(Product))!;

        Assert.Null(product.FindNavigation(nameof(AggregateRoot.DomainEvents)));
        Assert.Null(product.FindProperty(nameof(AggregateRoot.DomainEvents)));
    }

    [Fact]
    public void Outbox_IsMappedWithAJsonbPayload()
    {
        using var context = CreateContext();
        var outbox = context.Model.FindEntityType(typeof(OutboxMessage))!;

        Assert.Equal("OutboxMessages", outbox.GetTableName());
        Assert.Equal("jsonb", outbox.FindProperty(nameof(OutboxMessage.Payload))!.GetColumnType());
    }

    [Fact]
    public void Inbox_IsKeyedByMessageAndConsumer()
    {
        using var context = CreateContext();
        var key = context.Model.FindEntityType(typeof(InboxMessage))!.FindPrimaryKey()!;

        Assert.Equal([nameof(InboxMessage.MessageId), nameof(InboxMessage.Consumer)], key.Properties.Select(p => p.Name));
        Assert.Equal(InboxMessageConfiguration.PrimaryKeyName, key.GetName());
    }

    [Fact]
    public void AuditLog_IsMappedWithJsonbDetails()
    {
        using var context = CreateContext();
        var audit = context.Model.FindEntityType(typeof(AuditLogEntry))!;

        Assert.Equal("AuditLogEntries", audit.GetTableName());
        Assert.Equal("jsonb", audit.FindProperty(nameof(AuditLogEntry.Details))!.GetColumnType());
    }

    [Fact]
    public void OutboxLockQuery_UsesQuotedIdentifiersFromTheModel()
    {
        using var context = CreateContext();

        var sql = OutboxProcessor<TestDbContext>.LockBatchSql(context);

        Assert.Contains("FROM \"OutboxMessages\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"ProcessedAt\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY \"OccurredAt\"", sql, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE SKIP LOCKED", sql, StringComparison.Ordinal);
    }

    private static TestDbContext CreateContext()
        => new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
}
