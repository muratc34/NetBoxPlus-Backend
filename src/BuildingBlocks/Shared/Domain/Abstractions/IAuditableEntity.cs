namespace Shared.Domain.Abstractions
{
    public interface IAuditableEntity
    {
        DateTime CreatedOnUtc { get; }
        DateTime? ModifiedOnUtc { get; }
    }
}
