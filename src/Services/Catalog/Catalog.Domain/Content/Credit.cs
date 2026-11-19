using Shared.Domain;

namespace Catalog.Domain.Content;

public enum CreditRole
{
    Director,
    Writer,
    Actor,
    Creator,
}

/// <summary>A person's part in a movie or series, as an editor enters it.</summary>
public sealed record CreditInput(Guid PersonId, CreditRole Role, string? CharacterName, int Order);

/// <summary>A person's part in a movie or series. Part of that aggregate; the person is its own.</summary>
public sealed class Credit
{
    public const int CharacterNameMaxLength = 200;

    internal Credit(Guid personId, CreditRole role, string? characterName, int order)
    {
        PersonId = Ensure.NotEmpty(personId);
        Role = role;
        CharacterName = characterName;
        Order = order;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Credit()
    {
    }

    public Guid PersonId { get; private set; }

    public CreditRole Role { get; private set; }

    public string? CharacterName { get; private set; }

    /// <summary>Position in the cast list.</summary>
    public int Order { get; private set; }

    internal void Update(string? characterName, int order)
    {
        CharacterName = characterName;
        Order = order;
    }
}

/// <summary>A genre a movie or series belongs to. Part of that aggregate; the genre is its own.</summary>
public sealed class ContentGenre
{
    internal ContentGenre(Guid genreId) => GenreId = Ensure.NotEmpty(genreId);

    /// <summary>Used by EF Core when materializing.</summary>
    private ContentGenre()
    {
    }

    public Guid GenreId { get; private set; }
}
