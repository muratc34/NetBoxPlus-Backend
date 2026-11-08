using Catalog.Application.Images;
using Catalog.Domain.Content;
using Catalog.Domain.Images;
using Catalog.Domain.Shows;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Shows.AttachSeriesImage;

/// <summary>Puts an uploaded image in a slot, replacing what was there.</summary>
/// <param name="Version">The version the editor loaded.</param>
public sealed record AttachSeriesImageCommand(Guid SeriesId, ContentImageSlot Slot, Guid UploadId, int Version) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => SeriesId;
}

internal sealed class AttachSeriesImageHandler(ISeriesRepository repository, ImageAttacher attacher) : ICommandHandler<AttachSeriesImageCommand>
{
    public async Task<Result> Handle(AttachSeriesImageCommand command, CancellationToken cancellationToken)
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

        var use = command.Slot == ContentImageSlot.Poster ? ImageUse.Poster : ImageUse.Backdrop;
        var image = await attacher.ConsumeAsync(command.UploadId, use, cancellationToken).ConfigureAwait(false);
        if (image.IsFailure)
        {
            return image.Error;
        }

        await attacher.RetireAsync(series.ImageOf(command.Slot), cancellationToken).ConfigureAwait(false);
        series.SetImage(command.Slot, image.Value);
        return Result.Success();
    }
}
