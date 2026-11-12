using Catalog.Domain.Content;
using Catalog.Domain.Movies;
using Catalog.Domain.Shows;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Videos.ApplyVideoState;

/// <summary>Records what the streaming service reports about the video of a movie or an episode.</summary>
/// <param name="ContentKind"><c>Movie</c> or <c>Episode</c>, as the streaming service names it.</param>
public sealed record ApplyVideoStateCommand(string ContentKind, Guid ContentId, Guid VideoId, VideoState State, int Revision) : ICommand;

public static class VideoReportErrors
{
    public static readonly Error ContentKindUnknown = Error.Validation("Video.ContentKindUnknown", "The content kind must be Movie or Episode.");
}

internal sealed class ApplyVideoStateHandler(IMovieRepository movies, ISeriesRepository series) : ICommandHandler<ApplyVideoStateCommand>
{
    public async Task<Result> Handle(ApplyVideoStateCommand command, CancellationToken cancellationToken)
    {
        switch (command.ContentKind)
        {
            case "Movie":
                var movie = await movies.GetByIdAsync(command.ContentId, cancellationToken).ConfigureAwait(false);
                movie?.ApplyVideo(command.VideoId, command.State, command.Revision);
                return Result.Success();

            case "Episode":
                var owner = await series.GetByEpisodeIdAsync(command.ContentId, cancellationToken).ConfigureAwait(false);
                return owner?.ApplyEpisodeVideo(command.ContentId, command.VideoId, command.State, command.Revision) ?? Result.Success();

            default:
                return VideoReportErrors.ContentKindUnknown;
        }
    }
}
