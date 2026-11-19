using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Application.Playback.GetProgress;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback.Preferences;

/// <summary>The languages one of the user's profiles last chose in the player.</summary>
public sealed record GetPlaybackPreferencesQuery(Guid UserId, Guid ProfileId) : IQuery<PlaybackPreferences>;

/// <param name="AudioLanguage">A BCP 47 tag such as <c>en-US</c>; <see langword="null"/> for the original sound.</param>
/// <param name="SubtitleLanguage">A BCP 47 tag; <see langword="null"/> for subtitles off.</param>
public sealed record PlaybackPreferences(string? AudioLanguage, string? SubtitleLanguage);

/// <summary>Keeps the profile's choice; the next sessions start with it where the video offers it.</summary>
public sealed record SetPlaybackPreferencesCommand(Guid UserId, Guid ProfileId, string? AudioLanguage, string? SubtitleLanguage) : ICommand;

internal sealed class GetPlaybackPreferencesHandler(IStreamingReadModel readModel) : IQueryHandler<GetPlaybackPreferencesQuery, PlaybackPreferences>
{
    public async Task<Result<PlaybackPreferences>> Handle(GetPlaybackPreferencesQuery query, CancellationToken cancellationToken)
    {
        if (await ProfileOwnership.FindAsync(readModel, query.UserId, query.ProfileId, cancellationToken).ConfigureAwait(false) is null)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        var preference = await readModel.PlaybackPreferences.FirstOrDefaultAsync(item => item.Id == query.ProfileId, cancellationToken).ConfigureAwait(false);
        return new PlaybackPreferences(preference?.AudioLanguage, preference?.SubtitleLanguage);
    }
}

internal sealed class SetPlaybackPreferencesHandler(IStreamingReadModel readModel, IPlaybackPreferenceRepository preferences)
    : ICommandHandler<SetPlaybackPreferencesCommand>
{
    public async Task<Result> Handle(SetPlaybackPreferencesCommand command, CancellationToken cancellationToken)
    {
        if (await ProfileOwnership.FindAsync(readModel, command.UserId, command.ProfileId, cancellationToken).ConfigureAwait(false) is null)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        var preference = await preferences.GetAsync(command.ProfileId, cancellationToken).ConfigureAwait(false);
        if (preference is null)
        {
            // Two first choices at once: the primary key lets one through, the other gets a conflict.
            preference = PlaybackPreference.Create(command.ProfileId, command.UserId);
            preferences.Add(preference);
        }

        return preference.Choose(command.AudioLanguage, command.SubtitleLanguage);
    }
}
