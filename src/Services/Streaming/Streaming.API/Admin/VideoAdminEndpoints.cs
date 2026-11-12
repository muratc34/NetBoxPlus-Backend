using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;
using Streaming.Application.Videos.ConfirmVideoUpload;
using Streaming.Application.Videos.GetVideos;
using Streaming.Application.Videos.StartVideoUpload;
using Streaming.Domain.Videos;

namespace Streaming.API.Admin;

internal static class StreamingPermissions
{
    public const string VideosRead = "streaming:videos:read";
    public const string VideosWrite = "streaming:videos:write";
}

public sealed record StartVideoUploadRequest(ContentKind ContentKind, Guid ContentId);

/// <summary>Videos, for the admin panel: upload, then follow the conversion.</summary>
internal static class VideoAdminEndpoints
{
    public static IEndpointRouteBuilder MapVideoAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/videos").WithTags("Videos");

        group.MapPost("/", StartAsync)
            .WithName("StartVideoUpload")
            .WithSummary("Promises a video upload for a movie or an episode and returns where to send the file.")
            .RequirePermission(StreamingPermissions.VideosWrite)
            .Produces<VideoUploadTicket>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/uploaded", ConfirmAsync)
            .WithName("ConfirmVideoUpload")
            .WithSummary("Says the file is uploaded; the video is then converted in the background.")
            .RequirePermission(StreamingPermissions.VideosWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", GetVideosAsync)
            .WithName("GetVideos")
            .WithSummary("Lists videos, newest first.")
            .RequirePermission(StreamingPermissions.VideosRead)
            .Produces<Page<VideoSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetVideoAsync)
            .WithName("GetVideo")
            .WithSummary("Returns a video with its renditions, or why it could not be converted.")
            .RequirePermission(StreamingPermissions.VideosRead)
            .Produces<VideoDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> StartAsync([FromBody] StartVideoUploadRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new StartVideoUploadCommand(request.ContentKind, request.ContentId), cancellationToken);
        return result.ToHttpResult(ticket => TypedResults.Created($"/admin/videos/{ticket.VideoId}", ticket));
    }

    private static async Task<IResult> ConfirmAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new ConfirmVideoUploadCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetVideosAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] Guid? contentId = null,
        [FromQuery] VideoStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetVideosQuery(contentId, status, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetVideoAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetVideoQuery(id), cancellationToken)).ToHttpResult();
}
