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

    public override ContentKind Kind => ContentKind.Movie;

    public ContentVideo Video { get; private set; } = new();

    /// <summary>
    /// A report from the streaming service. Not an edit: the version stays, so that an admin who has
    /// the movie open is not told it changed.
    /// </summary>
    public void ApplyVideo(Guid videoId, VideoState state, int revision)
    {
        var playable = Video.PlayableVideoId;
        Video.Apply(videoId, state, revision);
        if (IsPublished && Video.PlayableVideoId != playable)
        {
            PlayabilityChanged();
        }
    }

    protected override Result CheckPlayable() => Video.IsPlayable ? Result.Success() : ContentErrors.VideoRequired;

    protected override IReadOnlyList<PlayableVideo> PlayableVideos()
        => Video.PlayableVideoId is { } videoId ? [new PlayableVideo(Id, videoId)] : [];

    public static Result<Movie> Create(ContentInfo info, IReadOnlyDictionary<string, ContentText> texts, int runtimeMinutes)
    {
        ArgumentNullException.ThrowIfNull(info);

        var runtime = Runtime.Check(runtimeMinutes);
        if (runtime.IsFailure)
        {
            return runtime.Error;
        }

        var movie = new Movie(Guid.CreateVersion7(), info, runtimeMinutes);
        var set = movie.SetTexts(texts);
        return set.IsFailure ? set.Error : movie;
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
