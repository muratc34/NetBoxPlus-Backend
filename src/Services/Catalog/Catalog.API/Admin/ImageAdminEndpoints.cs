using Catalog.Application.Images.RequestImageUpload;
using Catalog.Application.Movies.AttachMovieImage;
using Catalog.Application.Movies.RemoveMovieImage;
using Catalog.Application.People.AttachPersonPhoto;
using Catalog.Application.People.RemovePersonPhoto;
using Catalog.Application.Shows.AttachSeriesImage;
using Catalog.Application.Shows.RemoveSeriesImage;
using Catalog.Domain.Content;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Catalog.API.Admin;

/// <summary>
/// Images, for the admin panel. A file goes straight to storage through the address an upload
/// gives; attaching it then checks the file and puts it to use.
/// </summary>
internal static class ImageAdminEndpoints
{
    /// <summary>Lower case in the address; anything else is not a route.</summary>
    private const string SlotRoute = "{slot:regex(^(poster|backdrop)$)}";

    public static IEndpointRouteBuilder MapImageAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin").WithTags("Images");

        admin.MapPost("/images/uploads", RequestUploadAsync)
            .WithName("RequestImageUpload")
            .WithSummary("Starts an upload and returns the address to PUT the file to.")
            .RequirePermission(CatalogPermissions.ImagesUpload)
            .Produces<ImageUploadTicket>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        Attach(admin.MapPut($"/movies/{{id:guid}}/images/{SlotRoute}", AttachMovieAsync), "AttachMovieImage", CatalogPermissions.MoviesWrite);
        Remove(admin.MapDelete($"/movies/{{id:guid}}/images/{SlotRoute}", RemoveMovieAsync), "RemoveMovieImage", CatalogPermissions.MoviesWrite);
        Attach(admin.MapPut($"/series/{{id:guid}}/images/{SlotRoute}", AttachSeriesAsync), "AttachSeriesImage", CatalogPermissions.SeriesWrite);
        Remove(admin.MapDelete($"/series/{{id:guid}}/images/{SlotRoute}", RemoveSeriesAsync), "RemoveSeriesImage", CatalogPermissions.SeriesWrite);
        Attach(admin.MapPut("/people/{id:guid}/photo", AttachPhotoAsync), "AttachPersonPhoto", CatalogPermissions.PeopleWrite);
        Remove(admin.MapDelete("/people/{id:guid}/photo", RemovePhotoAsync), "RemovePersonPhoto", CatalogPermissions.PeopleWrite);

        return endpoints;
    }

    private static void Attach(RouteHandlerBuilder builder, string name, string permission)
        => builder
            .WithName(name)
            .WithSummary("Checks an uploaded file and puts it to use, replacing what was there.")
            .RequirePermission(permission)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static void Remove(RouteHandlerBuilder builder, string name, string permission)
        => builder
            .WithName(name)
            .WithSummary("Removes an image. A published title must keep its poster.")
            .RequirePermission(permission)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static ContentImageSlot Parse(string slot) => slot == "poster" ? ContentImageSlot.Poster : ContentImageSlot.Backdrop;

    private static async Task<IResult> RequestUploadAsync([FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RequestImageUploadCommand(), cancellationToken)).ToHttpResult();

    private static async Task<IResult> AttachMovieAsync(Guid id, string slot, [FromBody] AttachImageRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new AttachMovieImageCommand(id, Parse(slot), request.UploadId, request.Version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveMovieAsync(Guid id, string slot, [FromQuery] int version, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveMovieImageCommand(id, Parse(slot), version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> AttachSeriesAsync(Guid id, string slot, [FromBody] AttachImageRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new AttachSeriesImageCommand(id, Parse(slot), request.UploadId, request.Version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveSeriesAsync(Guid id, string slot, [FromQuery] int version, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveSeriesImageCommand(id, Parse(slot), version), cancellationToken)).ToHttpResult();

    private static async Task<IResult> AttachPhotoAsync(Guid id, [FromBody] AttachPhotoRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new AttachPersonPhotoCommand(id, request.UploadId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemovePhotoAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemovePersonPhotoCommand(id), cancellationToken)).ToHttpResult();
}
