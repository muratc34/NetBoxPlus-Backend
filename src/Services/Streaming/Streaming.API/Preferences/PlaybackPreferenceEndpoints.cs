using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;
using Streaming.Application.Playback.Preferences;

namespace Streaming.API.Preferences;

/// <param name="AudioLanguage">A BCP 47 tag such as <c>en-US</c>; <see langword="null"/> for the original sound.</param>
/// <param name="SubtitleLanguage">A BCP 47 tag; <see langword="null"/> for subtitles off.</param>
public sealed record SetPlaybackPreferencesRequest(string? AudioLanguage, string? SubtitleLanguage);

/// <summary>The languages a profile chose in the player, for its next sessions.</summary>
internal static class PlaybackPreferenceEndpoints
{
    public static IEndpointRouteBuilder MapPlaybackPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var preferences = endpoints.MapGroup("/profiles/{profileId:guid}/playback-preferences").WithTags("Playback").RequireAuthorization();

        preferences.MapGet(string.Empty, GetAsync)
            .WithName("GetPlaybackPreferences")
            .WithSummary("The languages of sound and subtitles the profile last chose.")
            .Produces<PlaybackPreferences>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        preferences.MapPut(string.Empty, SetAsync)
            .WithName("SetPlaybackPreferences")
            .WithSummary("Keeps the profile's choice; its next sessions start with it where the video offers the language.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid profileId, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetPlaybackPreferencesQuery(currentUser.Id!.Value, profileId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> SetAsync(Guid profileId, [FromBody] SetPlaybackPreferencesRequest request, [FromServices] IDispatcher dispatcher, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
        => (await dispatcher.Send(new SetPlaybackPreferencesCommand(currentUser.Id!.Value, profileId, request.AudioLanguage, request.SubtitleLanguage), cancellationToken)).ToHttpResult();
}
