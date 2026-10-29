using Shared.Domain;

namespace Sample.Domain.Notes;

public static class NoteErrors
{
    public static readonly Error TitleRequired = Error.Validation("Note.TitleRequired", "The title is required.");

    public static readonly Error TitleTooLong = Error.Validation(
        "Note.TitleTooLong",
        $"The title must be at most {NoteTitle.MaxLength} characters.");

    public static readonly Error NotFound = Error.NotFound("Note.NotFound", "The note was not found.");
}
