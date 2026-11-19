using Catalog.Application.Abstractions;
using Catalog.Domain.Images;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Catalog.Application.Images.RequestImageUpload;

/// <summary>Promises an upload and hands out the address to send the file to.</summary>
public sealed record RequestImageUploadCommand : ICommand<ImageUploadTicket>;

/// <param name="UploadAddress">Takes one PUT of the file, with the header <c>x-ms-blob-type: BlockBlob</c>.</param>
public sealed record ImageUploadTicket(Guid UploadId, Uri UploadAddress, DateTimeOffset ExpiresAt, long MaxBytes);

internal sealed class RequestImageUploadHandler(IImageFileRepository files, IImageStorage storage, TimeProvider timeProvider)
    : ICommandHandler<RequestImageUploadCommand, ImageUploadTicket>
{
    public Task<Result<ImageUploadTicket>> Handle(RequestImageUploadCommand command, CancellationToken cancellationToken)
    {
        var file = ImageFile.StartUpload(timeProvider.GetUtcNow());
        files.Add(file);

        return Task.FromResult(Result.Success(new ImageUploadTicket(
            file.Id,
            storage.CreateUploadAddress(file.Id, file.ExpiresAt),
            file.ExpiresAt,
            ImageRules.MaxBytes)));
    }
}
