using Shared.Domain;

namespace Catalog.Domain.People;

public static class PersonErrors
{
    public static readonly Error NotFound = Error.NotFound("Person.NotFound", "The person was not found.");

    public static readonly Error NameRequired = Error.Validation("Person.NameRequired", "The name is required.");

    public static readonly Error NameTooLong = Error.Validation("Person.NameTooLong", $"The name must be at most {Person.NameMaxLength} characters.");

    public static readonly Error BiographyTooLong = Error.Validation(
        "Person.BiographyTooLong",
        $"The biography must be at most {Person.BiographyMaxLength} characters.");
}
