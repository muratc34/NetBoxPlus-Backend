using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Domain.Access;

namespace Streaming.Application.Access;

/// <summary>The catalog's list of what can be watched of a title. Sent by the event handlers, not by people.</summary>
public sealed record ApplyPlayableCommand(Guid TitleId, TitleKind Kind, AgeRating AgeRating, IReadOnlyList<PlayableItem> Items, int Revision) : ICommand;

internal sealed class ApplyPlayableHandler(IPlayableTitleRepository titles) : ICommandHandler<ApplyPlayableCommand>
{
    public async Task<Result> Handle(ApplyPlayableCommand command, CancellationToken cancellationToken)
    {
        var title = await titles.GetByIdAsync(command.TitleId, cancellationToken).ConfigureAwait(false);
        if (title is null)
        {
            title = PlayableTitle.Create(command.TitleId, command.Kind);
            titles.Add(title);
        }

        title.Replace(command.AgeRating, command.Items, command.Revision);
        return Result.Success();
    }
}
