namespace Shared.Domain.Tests;

public sealed class EnsureTests
{
    [Fact]
    public void NotEmpty_ReturnsANonEmptyGuid()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(id, Ensure.NotEmpty(id));
    }

    [Fact]
    public void NotEmpty_WithEmptyGuid_ThrowsWithTheArgumentName()
    {
        var userId = Guid.Empty;

        var exception = Assert.Throws<ArgumentException>(() => Ensure.NotEmpty(userId));
        Assert.Equal(nameof(userId), exception.ParamName);
    }

    [Fact]
    public void NotDefault_ReturnsANonDefaultValue()
    {
        var now = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now, Ensure.NotDefault(now));
    }

    [Fact]
    public void NotDefault_WithDefault_ThrowsWithTheArgumentName()
    {
        DateTimeOffset startsAt = default;

        var exception = Assert.Throws<ArgumentException>(() => Ensure.NotDefault(startsAt));
        Assert.Equal(nameof(startsAt), exception.ParamName);
    }
}
