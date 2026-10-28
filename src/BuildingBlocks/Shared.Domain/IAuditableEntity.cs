namespace Shared.Domain;

/// <summary>
/// Entity whose creation and modification times are recorded. The values are written by the
/// persistence layer, not by domain code.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; }

    DateTimeOffset? ModifiedAt { get; }
}
