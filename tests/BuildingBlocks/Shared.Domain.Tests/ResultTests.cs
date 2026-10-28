namespace Shared.Domain.Tests;

public sealed class ResultTests
{
    private static readonly Error SomeError = Error.Failure("Test.Failed", "Something failed.");

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Same(Error.None, result.Error);
    }

    [Fact]
    public void Failure_CarriesTheError()
    {
        var result = Result.Failure(SomeError);

        Assert.True(result.IsFailure);
        Assert.Equal(SomeError, result.Error);
    }

    [Fact]
    public void Failure_WithNone_Throws()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
        Assert.Throws<ArgumentException>(() => Result.Failure<int>(Error.None));
    }

    [Fact]
    public void Failure_WithNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void Value_OnSuccess_ReturnsTheValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Value_OnFailure_Throws()
    {
        var result = Result.Failure<int>(SomeError);

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains(SomeError.Code, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        Result<string> result = "value";

        Assert.True(result.IsSuccess);
        Assert.Equal("value", result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromError_IsFailure()
    {
        Result result = SomeError;
        Result<string> typed = SomeError;

        Assert.True(result.IsFailure);
        Assert.True(typed.IsFailure);
        Assert.Equal(SomeError, typed.Error);
    }

    [Fact]
    public void FirstFailureOrSuccess_ReturnsTheFirstFailure()
    {
        var second = Error.Validation("Test.Second", "Second failure.");

        var result = Result.FirstFailureOrSuccess(
            Result.Success(),
            Result.Failure<int>(SomeError),
            Result.Failure(second));

        Assert.Equal(SomeError, result.Error);
    }

    [Fact]
    public void FirstFailureOrSuccess_WhenAllSucceed_IsSuccess()
    {
        var result = Result.FirstFailureOrSuccess(Result.Success(), Result.Success("value"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FirstFailureOrSuccess_WithNoResults_IsSuccess()
    {
        Assert.True(Result.FirstFailureOrSuccess().IsSuccess);
    }
}
