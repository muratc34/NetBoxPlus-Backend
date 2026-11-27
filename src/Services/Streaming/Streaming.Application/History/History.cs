using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Application.Validation;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Application.Playback;
using Streaming.Application.Playback.GetProgress;
using Streaming.Domain.Access;
using Streaming.Domain.Playback;

namespace Streaming.Application.History;

/// <summary>The titles one of the user's profiles has watched, the most recently watched first.</summary>
public sealed record GetHistoryQuery(Guid UserId, Guid ProfileId, int PageNumber, int PageSize) : IQuery<Page<HistoryItem>>;

/// <param name="WatchedAt">When the profile last watched any of the title.</param>
public sealed record HistoryItem(Guid TitleId, TitleKind Kind, DateTimeOffset WatchedAt);

/// <summary>
/// Forgets that the profile watched a title: it leaves the history, "continue watching" and what the
/// profile's recommendations are drawn from. The weekly popularity keeps its count; it never knew who watched.
/// </summary>
public sealed record RemoveFromHistoryCommand(Guid UserId, Guid ProfileId, Guid TitleId) : ICommand;

internal sealed class GetHistoryValidator : IValidator<GetHistoryQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetHistoryQuery instance)
    {
        if (!PageLimits.IsValid(instance.PageNumber, instance.PageSize))
        {
            yield return new ValidationFailure("Page", PageLimits.OutOfRange.Code, PageLimits.OutOfRange.Message);
        }
    }
}

/// <summary>
/// Like "my list", only what can be watched now and by this profile is shown; the rest stays and comes back
/// when it can be watched again.
/// </summary>
internal sealed class GetHistoryHandler(ProfileAccess profileAccess, IStreamingReadModel readModel) : IQueryHandler<GetHistoryQuery, Page<HistoryItem>>
{
    public async Task<Result<Page<HistoryItem>>> Handle(GetHistoryQuery query, CancellationToken cancellationToken)
    {
        var found = await profileAccess.FindAsync(query.UserId, query.ProfileId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var profile = found.Value;

        // The age is stored by name, so the allowed ones are listed: comparing names would go by the alphabet.
        var allowed = Enum.GetValues<AgeRating>().Where(profile.MayWatch).ToList();
        var watched = readModel.WatchProgress
            .Where(progress => progress.ProfileId == query.ProfileId)
            .GroupBy(progress => progress.TitleId)
            .Select(group => new { TitleId = group.Key, WatchedAt = group.Max(progress => progress.UpdatedAt) })
            .Join(
                readModel.PlayableTitles.Where(title => title.Items.Any() && allowed.Contains(title.AgeRating)),
                item => item.TitleId,
                title => title.Id,
                (item, title) => new { item.TitleId, title.Kind, item.WatchedAt });

        var total = await watched.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await watched
            .OrderByDescending(item => item.WatchedAt)
            .ThenBy(item => item.TitleId)
            .Skip(PageLimits.Offset(query.PageNumber, query.PageSize))
            .Take(query.PageSize)
            .Select(item => new HistoryItem(item.TitleId, item.Kind, item.WatchedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new Page<HistoryItem>(items, query.PageNumber, query.PageSize, total);
    }
}

/// <summary>A title the profile never watched changes nothing.</summary>
internal sealed class RemoveFromHistoryHandler(ProfileAccess profileAccess, IWatchProgressRepository progress) : ICommandHandler<RemoveFromHistoryCommand>
{
    public async Task<Result> Handle(RemoveFromHistoryCommand command, CancellationToken cancellationToken)
    {
        var found = await profileAccess.FindAsync(command.UserId, command.ProfileId, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        await progress.RemoveForTitleAsync(command.ProfileId, command.TitleId, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
