using Shared.Domain;

namespace Sample.Domain.Notes;

/// <summary>A note's title: trimmed, not empty, at most <see cref="MaxLength"/> characters.</summary>
public sealed record NoteTitle
{
    public const int MaxLength = 200;

    private NoteTitle(string value) => Value = value;

    public string Value { get; }

    public static Result<NoteTitle> Create(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return NoteErrors.TitleRequired;
        }

        if (trimmed.Length > MaxLength)
        {
            return NoteErrors.TitleTooLong;
        }

        return new NoteTitle(trimmed);
    }
}
