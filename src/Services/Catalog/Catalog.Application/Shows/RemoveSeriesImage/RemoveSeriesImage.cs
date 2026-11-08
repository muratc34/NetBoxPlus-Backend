using Catalog.Application.Images;
using Catalog.Domain.Content;
using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.RemoveSeriesImage;

/// <summary>Empties an image slot.</summary>
/// <param name="Version">The version the editor loaded.</param>
public sealed record RemoveSeriesImageCommand(Guid SeriesId, ContentImageSlot Slot, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class RemoveSeriesImageHandler(ISeriesRepository repository, ImageAttacher attacher) : ICommandHandler<RemoveSeriesImageCommand>
{
    public async Task<Result> Handle(RemoveSeriesImageCommand command, CancellationToken cancellationToken)
    {
        var series = await repository.GetByIdAsync(command.SeriesId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return SeriesErrors.NotFound;
        }

        var version = series.CheckVersion(command.Version);
        if (version.IsFailure)
        {
            return version;
        }

        var previous = series.ImageOf(command.Slot);
        var removed = series.RemoveImage(command.Slot);
        if (removed.IsFailure)
        {
            return removed;
        }

        await attacher.RetireAsync(previous, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
