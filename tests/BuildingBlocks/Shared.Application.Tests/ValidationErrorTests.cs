using Shared.Application.Validation;
using Shared.Domain;

namespace Shared.Application.Tests;

public sealed class ValidationErrorTests
{
    [Fact]
    public void ValidationError_IsAValidationTypedError()
    {
        var failure = new ValidationFailure("Email", "Register.EmailRequired", "Email is required.");

        var error = new ValidationError([failure]);

        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(ValidationError.ErrorCode, error.Code);
        Assert.Equal(failure, Assert.Single(error.Failures));
    }

    [Fact]
    public void ValidationError_WithoutFailures_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ValidationError([]));
    }

    [Fact]
    public void ValidationError_CanFailATypedResult()
    {
        Result<int> result = new ValidationError([new ValidationFailure("Id", "Code", "Message")]);

        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
    }
}
