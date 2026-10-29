using Microsoft.EntityFrameworkCore;
using Shared.Application.Events;
using Shared.Domain;

namespace Shared.Infrastructure.Persistence.Tests;

// A small service model: an auditable, soft-deletable aggregate with an owned value, and a
// plain entity that a domain event handler creates.

internal sealed record Money(decimal Amount, string Currency);

internal sealed record ProductCreated(Guid ProductId, string Name) : IDomainEvent;

internal sealed class Product : AggregateRoot, IAuditableEntity, ISoftDeletableEntity
{
    private Product(Guid id, string name, Money price)
        : base(id)
    {
        Name = name;
        Price = price;
    }

    private Product()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public Money Price { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static Product Create(string name, decimal amount)
    {
        var product = new Product(Guid.CreateVersion7(), name, new Money(amount, "TRY"));
        product.RaiseDomainEvent(new ProductCreated(product.Id, name));
        return product;
    }

    public void Rename(string name) => Name = name;
}

internal sealed class ProductLog : Entity
{
    public ProductLog(Guid id, Guid productId, string message)
        : base(id)
    {
        ProductId = productId;
        Message = message;
    }

    private ProductLog()
    {
    }

    public Guid ProductId { get; private set; }

    public string Message { get; private set; } = string.Empty;
}

internal sealed class ProductCreatedHandler(TestDbContext context) : IDomainEventHandler<ProductCreated>
{
    public Task Handle(ProductCreated domainEvent, CancellationToken cancellationToken)
    {
        context.Add(new ProductLog(Guid.CreateVersion7(), domainEvent.ProductId, $"created {domainEvent.Name}"));
        return Task.CompletedTask;
    }
}

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : ServiceDbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductLog> ProductLogs => Set<ProductLog>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.HasKey(p => p.Id);
            product.Property(p => p.Name).HasMaxLength(200);
            product.HasIndex(p => p.Name).IsUnique();
            product.OwnsOne(p => p.Price, price =>
            {
                price.Property(m => m.Amount).HasColumnName("PriceAmount");
                price.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
            });
        });

        modelBuilder.Entity<ProductLog>().HasKey(log => log.Id);
    }
}

/// <summary>Integration event published through the outbox in tests.</summary>
internal sealed record PriceChanged(Guid ProductId, decimal Amount);
