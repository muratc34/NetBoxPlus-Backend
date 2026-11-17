using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Application.Playback.GetProgress;
using Streaming.Domain.Access;
using Streaming.Domain.Lists;
using Streaming.Domain.Playback;

namespace Streaming.Application.Lists;

/// <summary>The titles on one of the user's profiles' list that it can watch now, most recently added first.</summary>
public sealed record GetMyListQuery(Guid UserId, Guid ProfileId) : IQuery<IReadOnlyList<MyListItem>>;

public sealed record MyListItem(Guid TitleId, TitleKind Kind, DateTimeOffset AddedAt);

/// <summary>Puts a title the profile can watch now on its list.</summary>
public sealed record AddToMyListCommand(Guid UserId, Guid ProfileId, Guid TitleId) : ICommand;

/// <summary>Takes a title off the profile's list.</summary>
public sealed record RemoveFromMyListCommand(Guid UserId, Guid ProfileId, Guid TitleId) : ICommand;

/// <summary>
/// A title that cannot be watched now, or not by this profile, stays on the list but is not shown:
/// it comes back when it can be watched again.
/// </summary>
internal sealed class GetMyListHandler(IStreamingReadModel readModel) : IQueryHandler<GetMyListQuery, IReadOnlyList<MyListItem>>
{
    public async Task<Result<IReadOnlyList<MyListItem>>> Handle(GetMyListQuery query, CancellationToken cancellationToken)
    {
        var profile = await ProfileOwnership.FindAsync(readModel, query.UserId, query.ProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        var entries = await readModel.MyListEntries
            .Where(entry => entry.ProfileId == query.ProfileId)
            .Join(readModel.PlayableTitles.Where(title => title.Items.Any()), entry => entry.TitleId, title => title.Id, (entry, title) => new { entry.TitleId, title.Kind, title.AgeRating, entry.AddedAt })
            .OrderByDescending(item => item.AddedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return entries
            .Where(item => profile.MayWatch(item.AgeRating))
            .Select(item => new MyListItem(item.TitleId, item.Kind, item.AddedAt))
            .ToList();
    }
}

/// <summary>
/// The same checks as playback, but no subscription is needed: a list is for choosing what to watch.
/// A title already on the list stays where it is.
/// </summary>
internal sealed class AddToMyListHandler(
    IStreamingReadModel readModel,
    IMyListRepository lists,
    TimeProvider timeProvider) : ICommandHandler<AddToMyListCommand>
{
    public async Task<Result> Handle(AddToMyListCommand command, CancellationToken cancellationToken)
    {
        var profile = await ProfileOwnership.FindAsync(readModel, command.UserId, command.ProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        var title = await readModel.PlayableTitles
            .Where(item => item.Id == command.TitleId && item.Items.Any())
            .Select(item => new { item.AgeRating })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (title is null)
        {
            return PlaybackErrors.NotAvailable;
        }

        if (!profile.MayWatch(title.AgeRating))
        {
            return PlaybackErrors.AgeRestricted;
        }

        var list = await lists.GetAsync(command.ProfileId, cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            // Two first adds at once are settled by the primary key: the loser retries (409 Data.Conflict).
            list = MyList.Create(command.ProfileId, command.UserId);
            lists.Add(list);
        }

        return list.Add(command.TitleId, timeProvider.GetUtcNow());
    }
}

/// <summary>A title that is not on the list, or a list not made yet, changes nothing.</summary>
internal sealed class RemoveFromMyListHandler(IStreamingReadModel readModel, IMyListRepository lists) : ICommandHandler<RemoveFromMyListCommand>
{
    public async Task<Result> Handle(RemoveFromMyListCommand command, CancellationToken cancellationToken)
    {
        if (await ProfileOwnership.FindAsync(readModel, command.UserId, command.ProfileId, cancellationToken).ConfigureAwait(false) is null)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        (await lists.GetAsync(command.ProfileId, cancellationToken).ConfigureAwait(false))?.Remove(command.TitleId);
        return Result.Success();
    }
}
