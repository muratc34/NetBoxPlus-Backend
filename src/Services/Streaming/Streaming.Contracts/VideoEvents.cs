namespace Streaming.Contracts;

// Video events carry the video's Revision, which goes up with every change; a consumer ignores one
// whose Revision is not higher than the last it applied for that video. Video ids are version 7
// Guids: a later upload has a greater id.

/// <summary>A video was uploaded for the content and waits to be converted.</summary>
/// <param name="ContentKind"><c>Movie</c> or <c>Episode</c>.</param>
public sealed record VideoQueued(Guid VideoId, string ContentKind, Guid ContentId, int Revision, DateTimeOffset OccurredAt);

/// <summary>The video is converted and can be played.</summary>
/// <param name="ContentKind"><c>Movie</c> or <c>Episode</c>.</param>
public sealed record VideoReady(Guid VideoId, string ContentKind, Guid ContentId, double DurationSeconds, int Revision, DateTimeOffset OccurredAt);

/// <summary>The video could not be converted; the content keeps any video it already had.</summary>
/// <param name="ContentKind"><c>Movie</c> or <c>Episode</c>.</param>
public sealed record VideoFailed(Guid VideoId, string ContentKind, Guid ContentId, string Reason, int Revision, DateTimeOffset OccurredAt);
