using Catalog.Domain.Content;
using Shared.Domain;

namespace Catalog.Domain.Movies;

public sealed class Movie : ContentItem
{
    private Movie(Guid id, ContentInfo info, int runtimeMinutes)
        : base(id, info)
    {
        RuntimeMinutes = runtimeMinutes;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Movie()
    {
    }

    public int RuntimeMinutes { get; private set; }

    public static Result<Movie> Create(ContentInfo info, int runtimeMinutes)
    {
        ArgumentNullException.ThrowIfNull(info);

        var runtime = Runtime.Check(runtimeMinutes);
        return runtime.IsFailure ? runtime.Error : new Movie(Guid.CreateVersion7(), info, runtimeMinutes);
    }

    public Result ChangeRuntime(int runtimeMinutes)
    {
        var runtime = Runtime.Check(runtimeMinutes);
        if (runtime.IsFailure)
        {
            return runtime;
        }

        RuntimeMinutes = runtimeMinutes;
        MarkChanged();
        return Result.Success();
    }
}
