using Shared.Domain;

namespace Catalog.Domain.Content;

public enum ContentKind
{
    Movie,
    Series,
}

/// <summary>A movie or series changed or went away; what customers can find may have to follow.</summary>
public sealed record ContentChangedDomainEvent(Guid ContentId, ContentKind Kind) : IDomainEvent;
