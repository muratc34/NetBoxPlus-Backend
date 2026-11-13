using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Domain;
using Shared.Infrastructure.API.Errors;
using Streaming.Application.Playback.EndPlayback;
using Streaming.Application.Playback.GetPlaylists;
using Streaming.Application.Playback.GetProgress;
using Streaming.Application.Playback.Heartbeat;
using Streaming.Application.Playback.StartPlayback;

namespace Streaming.API.Playback;

public sealed record StartPlaybackRequest(Guid ProfileId, Guid ContentId);

/// <param name="PositionSeconds">Where the player is in the video; recorded as the profile's progress.</param>
public sealed record HeartbeatRequest(double? PositionSeconds);

/// <summary>Watching: sessions for signed-in customers, and the signed playlists their players fetch.</summary>
internal static class PlaybackEndpoints
{
    private const string PlaylistContentType = "application/vnd.apple.mpegurl";

    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sessions = endpoints.MapGroup("/playback-sessions").WithTags("Playback").RequireAuthorization();

        sessions.MapPost("/", StartAsync)
            .WithName("StartPlayback")
            .WithSummary("Starts watching a movie or an episode with a profile; returns the master playlist, relative to this address.")
            .Produces<PlaybackStarted>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        sessions.MapPost("/{id:guid}/heartbeat", HeartbeatAsync)
            .WithName("PlaybackHeartbeat")
            .WithSummary("Keeps the session live; without one for two minutes it ends. Fails, and ends it, when watching is no longer allowed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        sessions.MapDelete("/{id:guid}", EndAsync)
            .WithName("EndPlayback")
            .WithSummary("Stops watching, at the position given; the screen is free at once.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var profiles = endpoints.MapGroup("/profiles/{profileId:guid}").WithTags("Playback").RequireAuthorization();

        profiles.MapGet("/progress", ProgressAsync)
            .WithName("GetWatchProgress")
            .WithSummary("How far the profile has watched each movie or episode of a title.")
            .Produces<IReadOnlyList<ProgressView>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        profiles.MapGet("/continue-watching", ContinueWatchingAsync)
            .WithName("GetContinueWatching")
            .WithSummary("What the profile was watching, most recent first: the movie or episode to go on with, and where.")
            .Produces<IReadOnlyList<ContinueWatchingItem>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // No token here: players fetch these addresses as they are, and the signature in them is the permission.
        var playlists = endpoints.MapGroup("/playback/{sessionId:guid}").WithTags("Playback").AllowAnonymous();

        playlists.MapGet("/master.m3u8", MasterAsync)
            .WithName("GetMasterPlaylist")
            .WithSummary("The session's master playlist: the renditions its plan allows.")
            .Produces<string>(StatusCodes.Status200OK, PlaylistContentType)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        playlists.MapGet("/{rendition:regex(^[0-9]{{3,4}}p$)}/index.m3u8", RenditionAsync)
            .WithName("GetRenditionPlaylist")
            .WithSummary("One rendition's playlist, its segments as short-lived storage addresses.")
            .Produces<string>(StatusCodes.Status200OK, PlaylistContentType)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> StartAsync([FromBody] StartPlaybackRequest request, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new StartPlaybackCommand(currentUser.Id!.Value, request.ProfileId, request.ContentId), cancellationToken);
        return result.ToHttpResult(started => TypedResults.Created($"/playback-sessions/{started.SessionId}", started));
    }

    private static async Task<IResult> HeartbeatAsync(Guid id, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken, [FromBody] HeartbeatRequest? request = null)
        => (await dispatcher.Send(new HeartbeatCommand(currentUser.Id!.Value, id, request?.PositionSeconds), cancellationToken)).ToHttpResult();

    private static async Task<IResult> EndAsync(Guid id, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken, [FromQuery] double? positionSeconds = null)
        => (await dispatcher.Send(new EndPlaybackCommand(currentUser.Id!.Value, id, positionSeconds), cancellationToken)).ToHttpResult();

    private static async Task<IResult> ProgressAsync(Guid profileId, [FromQuery] Guid titleId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetProgressQuery(currentUser.Id!.Value, profileId, titleId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> ContinueWatchingAsync(Guid profileId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetContinueWatchingQuery(currentUser.Id!.Value, profileId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> MasterAsync(Guid sessionId, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] long? exp = null, [FromQuery] string? sig = null)
        => Playlist(context, await dispatcher.Query(new GetMasterPlaylistQuery(sessionId, exp, sig), cancellationToken));

    private static async Task<IResult> RenditionAsync(Guid sessionId, string rendition, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] long? exp = null, [FromQuery] string? sig = null)
        => Playlist(context, await dispatcher.Query(new GetRenditionPlaylistQuery(sessionId, rendition, exp, sig), cancellationToken));

    private static IResult Playlist(HttpContext context, Result<string> result)
    {
        // Signed and bound to a live session: never kept by a browser or a proxy.
        context.Response.Headers.CacheControl = "no-store";
        return result.ToHttpResult(text => Results.Text(text, PlaylistContentType));
    }
}
