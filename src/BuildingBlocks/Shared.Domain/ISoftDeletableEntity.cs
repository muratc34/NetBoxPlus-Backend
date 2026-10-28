namespace Shared.Domain;

/// <summary>
/// Entity that is flagged as deleted instead of being removed. The values are written by the
/// persistence layer, not by domain code.
/// </summary>
public interface ISoftDeletableEntity
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }
}
