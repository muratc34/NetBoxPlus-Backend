namespace Shared.Domain.Tests;

public sealed class EntityTests
{
    [Fact]
    public void SameTypeAndId_AreEqual()
    {
        var id = Guid.CreateVersion7();

        var left = new Movie(id);
        var right = new Movie(id);

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void DifferentIds_AreNotEqual()
    {
        Assert.NotEqual(new Movie(Guid.CreateVersion7()), new Movie(Guid.CreateVersion7()));
    }

    [Fact]
    public void DifferentTypesWithSameId_AreNotEqual()
    {
        var id = Guid.CreateVersion7();

        Assert.False(new Movie(id).Equals(new Series(id)));
    }

    [Fact]
    public void EntitiesWithoutId_AreNotEqual()
    {
        var left = new Movie();
        var right = new Movie();

        Assert.NotEqual(left, right);
        Assert.Equal(left, left);
    }

    [Fact]
    public void ComparisonWithNull_IsHandled()
    {
        Movie? missing = null;
        var movie = new Movie(Guid.CreateVersion7());

        Assert.False(movie.Equals(null));
        Assert.False(movie == missing);
        Assert.True(missing == null);
    }

    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Movie(Guid.Empty));
        Assert.Equal("id", exception.ParamName);
    }

    private sealed class Movie : Entity
    {
        public Movie(Guid id)
            : base(id)
        {
        }

        public Movie()
        {
        }
    }

    private sealed class Series(Guid id) : Entity(id);
}
