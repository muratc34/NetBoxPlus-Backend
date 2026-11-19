using Catalog.Application.Videos.ApplyVideoState;
using Catalog.Domain.Movies;
using Catalog.Domain.Shows;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Videos.ApplyVideoTracks;

/// <summary>Records the languages of sound and subtitles a video of a movie or an episode offers.</summary>
/// <param name="ContentKind"><c>Movie</c> or <c>Episode</c>, as the streaming service names it.</param>
public sealed record ApplyVideoTracksCommand(string ContentKind, Guid ContentId, Guid VideoId, IReadOnlyList<string> AudioLanguages, IReadOnlyList<string> SubtitleLanguages, int Revision) : ICommand;

internal sealed class ApplyVideoTracksHandler(IMovieRepository movies, ISeriesRepository series) : ICommandHandler<ApplyVideoTracksCommand>
{
    public async Task<Result> Handle(ApplyVideoTracksCommand command, CancellationToken cancellationToken)
    {
        switch (command.ContentKind)
        {
            case "Movie":
                var movie = await movies.GetByIdAsync(command.ContentId, cancellationToken).ConfigureAwait(false);
                movie?.ApplyVideoTracks(command.VideoId, command.AudioLanguages, command.SubtitleLanguages, command.Revision);
                return Result.Success();

            case "Episode":
                var owner = await series.GetByEpisodeIdAsync(command.ContentId, cancellationToken).ConfigureAwait(false);
                return owner?.ApplyEpisodeVideoTracks(command.ContentId, command.VideoId, command.AudioLanguages, command.SubtitleLanguages, command.Revision) ?? Result.Success();

            default:
                return VideoReportErrors.ContentKindUnknown;
        }
    }
}
