using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Domain;
using Shared.Infrastructure.API.Errors;
using Streaming.Application.Trailers;

namespace Streaming.API.Trailers;

/// <summary>
/// Trailers for anyone, signed in or not: a title's trailer link and the signed playlists behind it. No session,
/// subscription or screen limit; only titles that can be watched have one.
/// </summary>
internal static class TrailerEndpoints
{
    private const string PlaylistContentType = "application/vnd.apple.mpegurl";

    public static IEndpointRouteBuilder MapTrailerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/titles/{titleId:guid}/trailer", GetAsync)
            .WithTags("Trailers")
            .AllowAnonymous()
            .WithName("GetTrailer")
            .WithSummary("The newest trailer of a movie or a series that can be watched: a signed master playlist address.")
            .Produces<TrailerLink>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // No token here: players fetch these addresses as they are, and the signature in them is the permission.
        var playlists = endpoints.MapGroup("/trailers/{videoId:guid}").WithTags("Trailers").AllowAnonymous();

        playlists.MapGet("/master.m3u8", MasterAsync)
            .WithName("GetTrailerMasterPlaylist")
            .WithSummary("A trailer's master playlist: its renditions and its sound.")
            .Produces<string>(StatusCodes.Status200OK, PlaylistContentType)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        playlists.MapGet("/{rendition:regex(^[0-9]{{3,4}}p$)}/index.m3u8", RenditionAsync)
            .WithName("GetTrailerRenditionPlaylist")
            .WithSummary("One rendition of a trailer, its segments as short-lived storage addresses.")
            .Produces<string>(StatusCodes.Status200OK, PlaylistContentType)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        playlists.MapGet("/audio/original/index.m3u8", SoundAsync)
            .WithName("GetTrailerAudioPlaylist")
            .WithSummary("A trailer's sound, its segments as short-lived storage addresses.")
            .Produces<string>(StatusCodes.Status200OK, PlaylistContentType)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid titleId, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        // The link is signed for an hour: a shared cache must not hand one viewer's to the next.
        context.Response.Headers.CacheControl = "no-store";
        return (await dispatcher.Query(new GetTrailerQuery(titleId), cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> MasterAsync(Guid videoId, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] long? exp = null, [FromQuery] string? sig = null)
        => Playlist(context, await dispatcher.Query(new GetTrailerMasterPlaylistQuery(videoId, exp, sig), cancellationToken));

    private static async Task<IResult> RenditionAsync(Guid videoId, string rendition, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] long? exp = null, [FromQuery] string? sig = null)
        => Playlist(context, await dispatcher.Query(new GetTrailerRenditionPlaylistQuery(videoId, rendition, exp, sig), cancellationToken));

    private static async Task<IResult> SoundAsync(Guid videoId, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken, [FromQuery] long? exp = null, [FromQuery] string? sig = null)
        => Playlist(context, await dispatcher.Query(new GetTrailerRenditionPlaylistQuery(videoId, "audio/original", exp, sig), cancellationToken));

    private static IResult Playlist(HttpContext context, Result<string> result)
    {
        // Signed addresses inside: never kept by a browser or a proxy.
        context.Response.Headers.CacheControl = "no-store";
        return result.ToHttpResult(text => Results.Text(text, PlaylistContentType));
    }
}
