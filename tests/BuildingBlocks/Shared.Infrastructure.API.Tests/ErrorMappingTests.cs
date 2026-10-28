using Microsoft.AspNetCore.Http;
using Shared.Application.Validation;
using Shared.Domain;
using Shared.Infrastructure.API.Errors;

namespace Shared.Infrastructure.API.Tests;

public sealed class ErrorMappingTests
{
    private static readonly ErrorResponseOptions Options = new() { TypeBaseUri = new Uri("https://errors.test/") };

    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError)]
    public void ErrorType_MapsToItsStatusCode(ErrorType type, int expected)
    {
        Assert.Equal(expected, ErrorMapping.ToStatusCode(type));
    }

    [Fact]
    public void ErrorTypeNone_HasNoStatusCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ErrorMapping.ToStatusCode(ErrorType.None));
    }

    [Theory]
    [InlineData("User.DuplicateEmail", "user-duplicate-email")]
    [InlineData("Validation.Failed", "validation-failed")]
    [InlineData("Catalog.HTTPError", "catalog-http-error")]
    [InlineData("Order.Item_Missing", "order-item-missing")]
    [InlineData("Plan.Tier2Unavailable", "plan-tier2-unavailable")]
    public void ToKebabCase_TurnsCodesIntoUriSegments(string code, string expected)
    {
        Assert.Equal(expected, ErrorMapping.ToKebabCase(code));
    }

    [Fact]
    public void ProblemDetails_CarryStatusTitleDetailTypeAndCode()
    {
        var error = Error.Conflict("User.DuplicateEmail", "The email is already in use.");

        var problem = ErrorMapping.ToProblemDetails(error, Options);

        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Equal("The email is already in use.", problem.Detail);
        Assert.Equal("https://errors.test/user-duplicate-email", problem.Type);
        Assert.Equal("User.DuplicateEmail", problem.Extensions[ErrorMapping.CodeExtension]);
    }

    [Fact]
    public void ValidationError_ListsFailuresPerCamelCasedProperty()
    {
        var error = new ValidationError(
        [
            new ValidationFailure("Email", "Register.EmailRequired", "Email is required."),
            new ValidationFailure("Email", "Register.EmailFormat", "Email is invalid."),
            new ValidationFailure("FirstName", "Register.FirstNameRequired", "First name is required."),
        ]);

        var problem = Assert.IsType<HttpValidationProblemDetails>(ErrorMapping.ToProblemDetails(error, Options));

        Assert.Equal(400, problem.Status);
        Assert.Equal(["Email is required.", "Email is invalid."], problem.Errors["email"]);
        Assert.Equal(["First name is required."], problem.Errors["firstName"]);

        var codes = Assert.IsType<Dictionary<string, string[]>>(problem.Extensions[ErrorMapping.ErrorCodesExtension]);
        Assert.Equal(["Register.EmailRequired", "Register.EmailFormat"], codes["email"]);
    }

    [Fact]
    public void ToProblem_WithNone_Throws()
    {
        Assert.Throws<ArgumentException>(() => Error.None.ToProblem());
    }
}
