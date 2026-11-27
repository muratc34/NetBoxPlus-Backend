using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Application.Playback;
using Streaming.Application.Playback.GetProgress;
using Streaming.Domain.Playback;
using Streaming.Domain.Ratings;

namespace Streaming.Application.Ratings;

/// <summary>What one of the user's profiles is drawn to and away from, for the catalog to recommend from.</summary>
public sealed record GetTasteQuery(Guid UserId, Guid ProfileId) : IQuery<Taste>;

internal sealed class GetTasteHandler(ProfileAccess profileAccess, IStreamingReadModel readModel) : IQueryHandler<GetTasteQuery, Taste>
{
    // Enough rows to fill every list even when some titles cannot be watched now.
    private const int RecentRows = 200;

    public async Task<Result<Taste>> Handle(GetTasteQuery query, CancellationToken cancellationToken)
    {
        var found = await profileAccess.FindAsync(query.UserId, query.ProfileId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var ratings = await readModel.TitleRatings
            .Where(rating => rating.ProfileId == query.ProfileId)
            .OrderByDescending(rating => rating.RatedAt)
            .Take(RecentRows)
            .Select(rating => new { rating.TitleId, rating.Level })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var watched = await readModel.WatchProgress
            .Where(progress => progress.ProfileId == query.ProfileId)
            .GroupBy(progress => progress.TitleId)
            .Select(group => new { TitleId = group.Key, WatchedAt = group.Max(progress => progress.UpdatedAt) })
            .OrderByDescending(item => item.WatchedAt)
            .Take(RecentRows)
            .Select(item => item.TitleId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var candidates = ratings.Select(rating => rating.TitleId).Concat(watched).Distinct().ToList();
        var playable = (await readModel.PlayableTitles
            .Where(title => candidates.Contains(title.Id) && title.Items.Any())
            .Select(title => title.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

        return Tastes.Order([.. ratings.Select(rating => (rating.TitleId, rating.Level))], watched, playable);
    }
}
