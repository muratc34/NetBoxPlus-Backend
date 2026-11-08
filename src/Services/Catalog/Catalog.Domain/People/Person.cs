using Shared.Domain;

namespace Catalog.Domain.People;

/// <summary>Someone credited on movies or series. Cannot be deleted while credited.</summary>
public sealed class Person : AggregateRoot, IAuditableEntity
{
    public const int NameMaxLength = 200;
    public const int BiographyMaxLength = 4000;

    private Person(Guid id, string name, string? biography)
        : base(id)
    {
        Name = name;
        Biography = biography;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Person()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Biography { get; private set; }

    /// <summary>Name of the stored photo, when there is one.</summary>
    public string? PhotoImage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Result<Person> Create(string? name, string? biography)
    {
        var checkedValues = Check(name, biography);
        return checkedValues.IsFailure
            ? checkedValues.Error
            : new Person(Guid.CreateVersion7(), checkedValues.Value.Name, checkedValues.Value.Biography);
    }

    public Result Change(string? name, string? biography)
    {
        var checkedValues = Check(name, biography);
        if (checkedValues.IsFailure)
        {
            return checkedValues.Error;
        }

        Name = checkedValues.Value.Name;
        Biography = checkedValues.Value.Biography;
        return Result.Success();
    }

    public void SetPhoto(string? imageName) => PhotoImage = imageName;

    private static Result<(string Name, string? Biography)> Check(string? name, string? biography)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return PersonErrors.NameRequired;
        }

        if (trimmedName.Length > NameMaxLength)
        {
            return PersonErrors.NameTooLong;
        }

        var trimmedBiography = string.IsNullOrWhiteSpace(biography) ? null : biography.Trim();
        if (trimmedBiography?.Length > BiographyMaxLength)
        {
            return PersonErrors.BiographyTooLong;
        }

        return (trimmedName, trimmedBiography);
    }
}
