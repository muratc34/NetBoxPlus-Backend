using Catalog.Domain.Content;
using Shared.Application.Validation;

namespace Catalog.Application.Content;

/// <summary>What an editor fills in for a movie or series; saving replaces all of it.</summary>
public sealed record ContentFields(
    string? Title,
    string? OriginalTitle,
    string? Synopsis,
    int ReleaseYear,
    AgeRating AgeRating,
    IReadOnlyList<Guid> GenreIds,
    IReadOnlyList<CreditInput> Credits)
{
    internal ContentInfo ToInfo() => ContentInfo.Create(Title, OriginalTitle, Synopsis, ReleaseYear, AgeRating).Value;

    internal IEnumerable<ValidationFailure> Validate()
        => ContentInfo.Check(Title, OriginalTitle, Synopsis, ReleaseYear, AgeRating)
            .Select(problem => new ValidationFailure(problem.Field, problem.Error.Code, problem.Error.Message));
}

public sealed record GenreReference(Guid Id, string Name);

public sealed record CreditResponse(Guid PersonId, string PersonName, CreditRole Role, string? CharacterName, int Order);
