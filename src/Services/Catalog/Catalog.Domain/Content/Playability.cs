using Shared.Domain;

namespace Catalog.Domain.Content;

/// <summary>A movie or an episode customers can watch, and the video that plays for it.</summary>
public sealed record PlayableVideo(Guid ContentId, Guid VideoId);

/// <summary>What customers can watch of a title now; empty when it is not published.</summary>
public sealed record Playability(Guid TitleId, ContentKind Kind, AgeRating AgeRating, IReadOnlyList<PlayableVideo> Videos, int Revision);

/// <summary>
/// What can be watched of the title changed: its published videos or its age rating. Raised at most
/// once per save; the title is read when the change is saved, so the last state is announced.
/// </summary>
public sealed record PlayabilityChangedDomainEvent(ContentItem Title) : IDomainEvent;
