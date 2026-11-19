using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billing.Infrastructure.Idempotency;

/// <summary>A request remembered by its client's Idempotency-Key; kept for a day.</summary>
internal sealed class IdempotencyRequest
{
    public const int KeyMaxLength = 255;

    public Guid UserId { get; init; }

    public string Key { get; init; } = string.Empty;

    /// <summary>SHA-256 of method, path and body, hex: a key reused for another request is refused.</summary>
    public string RequestHash { get; init; } = string.Empty;

    public bool Completed { get; init; }

    public int? StatusCode { get; init; }

    public string? ResponseBody { get; init; }

    public DateTimeOffset StartedAt { get; init; }
}

internal sealed class IdempotencyRequestConfiguration : IEntityTypeConfiguration<IdempotencyRequest>
{
    public void Configure(EntityTypeBuilder<IdempotencyRequest> builder)
    {
        builder.ToTable("IdempotencyRequests");
        builder.HasKey(request => new { request.UserId, request.Key });
        builder.Property(request => request.Key).HasMaxLength(IdempotencyRequest.KeyMaxLength);
        builder.Property(request => request.RequestHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(request => request.StartedAt);
    }
}
