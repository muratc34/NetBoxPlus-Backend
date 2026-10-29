namespace Sample.Contracts;

/// <summary>Published when a note is created. Consumers find it by its full type name; never rename or move it.</summary>
public sealed record NoteCreated(Guid NoteId, string Title);
