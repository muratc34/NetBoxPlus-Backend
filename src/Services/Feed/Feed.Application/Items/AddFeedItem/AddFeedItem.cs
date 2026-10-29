using Feed.Domain.Items;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Feed.Application.Items.AddFeedItem;

/// <summary>Adds a note to the feed. Adding a note that is already there succeeds without a change.</summary>
public sealed record AddFeedItemCommand(Guid NoteId, string Title) : ICommand;

internal sealed class AddFeedItemValidator : IValidator<AddFeedItemCommand>
{
    public IEnumerable<ValidationFailure> Validate(AddFeedItemCommand instance)
    {
        // Only the shape of the request; the item's rules live in FeedItem.
        if (instance.NoteId == Guid.Empty)
        {
            yield return new ValidationFailure(nameof(instance.NoteId), FeedItemErrors.NoteIdRequired.Code, FeedItemErrors.NoteIdRequired.Message);
        }

        if (string.IsNullOrWhiteSpace(instance.Title))
        {
            yield return new ValidationFailure(nameof(instance.Title), FeedItemErrors.TitleRequired.Code, FeedItemErrors.TitleRequired.Message);
        }
    }
}

internal sealed class AddFeedItemHandler(IFeedItemRepository items) : ICommandHandler<AddFeedItemCommand>
{
    public async Task<Result> Handle(AddFeedItemCommand command, CancellationToken cancellationToken)
    {
        // The inbox already drops redelivered messages; this also covers the same note arriving in
        // two different messages.
        if (await items.ExistsForNoteAsync(command.NoteId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Success();
        }

        var item = FeedItem.Create(command.NoteId, command.Title);
        if (item.IsFailure)
        {
            return item.Error;
        }

        items.Add(item.Value);
        return Result.Success();
    }
}
