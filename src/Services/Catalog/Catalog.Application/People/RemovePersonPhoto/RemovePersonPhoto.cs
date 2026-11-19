using Catalog.Application.Images;
using Catalog.Domain.People;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.People.RemovePersonPhoto;

public sealed record RemovePersonPhotoCommand(Guid PersonId) : ICommand, IAuditedCommand
{
    Guid? IAuditedCommand.AuditTargetId => PersonId;
}

internal sealed class RemovePersonPhotoHandler(IPersonRepository people, ImageAttacher attacher) : ICommandHandler<RemovePersonPhotoCommand>
{
    public async Task<Result> Handle(RemovePersonPhotoCommand command, CancellationToken cancellationToken)
    {
        var person = await people.GetByIdAsync(command.PersonId, cancellationToken).ConfigureAwait(false);
        if (person is null)
        {
            return PersonErrors.NotFound;
        }

        await attacher.RetireAsync(person.PhotoImage, cancellationToken).ConfigureAwait(false);
        person.SetPhoto(null);
        return Result.Success();
    }
}
