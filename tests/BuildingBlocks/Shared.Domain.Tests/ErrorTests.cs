namespace Shared.Domain.Tests;

public sealed class ErrorTests
{
    public static TheoryData<Func<string, string, Error>, ErrorType> Factories => new()
    {
        { Error.Failure, ErrorType.Failure },
        { Error.Validation, ErrorType.Validation },
        { Error.Unauthorized, ErrorType.Unauthorized },
        { Error.Forbidden, ErrorType.Forbidden },
        { Error.NotFound, ErrorType.NotFound },
        { Error.Conflict, ErrorType.Conflict },
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void Factory_SetsCodeMessageAndType(Func<string, string, Error> factory, ErrorType expectedType)
    {
        var error = factory("User.NotFound", "The user was not found.");

        Assert.Equal("User.NotFound", error.Code);
        Assert.Equal("The user was not found.", error.Message);
        Assert.Equal(expectedType, error.Type);
    }

    [Fact]
    public void None_HasTypeNoneAndEmptyCode()
    {
        Assert.Equal(ErrorType.None, Error.None.Type);
        Assert.Equal(string.Empty, Error.None.Code);
    }

    [Fact]
    public void Constructor_WithTypeNone_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Error("Code", "Message", ErrorType.None));
    }

    [Fact]
    public void Constructor_WithUnknownType_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Error("Code", "Message", (ErrorType)999));
    }

    [Theory]
    [InlineData("", "Message")]
    [InlineData(" ", "Message")]
    [InlineData("Code", "")]
    [InlineData("Code", " ")]
    public void Constructor_WithBlankCodeOrMessage_Throws(string code, string message)
    {
        Assert.Throws<ArgumentException>(() => new Error(code, message, ErrorType.Failure));
    }

    [Fact]
    public void Errors_WithSameValues_AreEqual()
    {
        Assert.Equal(Error.NotFound("A", "B"), Error.NotFound("A", "B"));
        Assert.NotEqual(Error.NotFound("A", "B"), Error.Conflict("A", "B"));
    }
}
