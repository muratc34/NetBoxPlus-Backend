using Feed.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Feed.Application.Items.GetFeed;

/// <summary>The most recently added items, newest first.</summary>
public sealed record GetFeedQuery(int Limit = GetFeedQuery.DefaultLimit) : IQuery<IReadOnlyList<FeedItemResponse>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
}

public sealed record FeedItemResponse(Guid Id, Guid NoteId, string Title, DateTimeOffset AddedAt);

internal sealed class GetFeedValidator : IValidator<GetFeedQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetFeedQuery instance)
    {
        if (instance.Limit is < 1 or > GetFeedQuery.MaxLimit)
        {
            yield return new ValidationFailure(
                nameof(instance.Limit),
                "Feed.LimitOutOfRange",
                $"The limit must be between 1 and {GetFeedQuery.MaxLimit}.");
        }
    }
}

internal sealed class GetFeedHandler(IFeedReadModel readModel) : IQueryHandler<GetFeedQuery, IReadOnlyList<FeedItemResponse>>
{
    public async Task<Result<IReadOnlyList<FeedItemResponse>>> Handle(GetFeedQuery query, CancellationToken cancellationToken)
    {
        var items = await readModel.Items
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(query.Limit)
            .Select(item => new FeedItemResponse(item.Id, item.NoteId, item.Title, item.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return items;
    }
}
