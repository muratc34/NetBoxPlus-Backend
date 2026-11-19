using Catalog.Application.Images;
using Catalog.Domain.Images;
using Catalog.Domain.People;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.People.AttachPersonPhoto;

public sealed record AttachPersonPhotoCommand(Guid PersonId, Guid UploadId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => PersonId;
}

internal sealed class AttachPersonPhotoHandler(IPersonRepository people, ImageAttacher attacher) : ICommandHandler<AttachPersonPhotoCommand>
{
    public async Task<Result> Handle(AttachPersonPhotoCommand command, CancellationToken cancellationToken)
    {
        var person = await people.GetByIdAsync(command.PersonId, cancellationToken).ConfigureAwait(false);
        if (person is null)
        {
            return PersonErrors.NotFound;
        }

        var image = await attacher.ConsumeAsync(command.UploadId, ImageUse.Photo, cancellationToken).ConfigureAwait(false);
        if (image.IsFailure)
        {
            return image.Error;
        }

        await attacher.RetireAsync(person.PhotoImage, cancellationToken).ConfigureAwait(false);
        person.SetPhoto(image.Value);
        return Result.Success();
    }
}
