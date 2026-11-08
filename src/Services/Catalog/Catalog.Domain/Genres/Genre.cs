using Shared.Domain;

namespace Catalog.Domain.Genres;

/// <summary>A genre, unique by name whatever the case. Cannot be deleted while content uses it.</summary>
public sealed class Genre : AggregateRoot, IAuditableEntity
{
    public const int NameMaxLength = 64;

    private Genre(Guid id, string name)
        : base(id)
    {
        Name = name;
        NormalizedName = Normalize(name);
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Genre()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The name in upper case by Turkish rules; unique.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Result<Genre> Create(string? name)
    {
        var checkedName = CheckName(name);
        return checkedName.IsFailure ? checkedName.Error : new Genre(Guid.CreateVersion7(), checkedName.Value);
    }

    public Result Rename(string? name)
    {
        var checkedName = CheckName(name);
        if (checkedName.IsFailure)
        {
            return checkedName.Error;
        }

        Name = checkedName.Value;
        NormalizedName = Normalize(Name);
        return Result.Success();
    }

    /// <summary>
    /// Upper case with Turkish i and ı handled first, so that "ilk" and "İlk" are the same name.
    /// Written out rather than using the tr-TR culture, which the chiseled image has no data for.
    /// </summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().Replace('i', 'İ').Replace('ı', 'I').ToUpperInvariant();
    }

    private static Result<string> CheckName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return GenreErrors.NameRequired;
        }

        return trimmed.Length > NameMaxLength ? GenreErrors.NameTooLong : trimmed;
    }
}
