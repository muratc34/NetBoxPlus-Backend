using Shared.Domain;
using Streaming.Domain.Videos;

namespace Streaming.Domain.Playback;

/// <summary>
/// The languages a profile last chose in the player: what its sessions start with when the video
/// offers them. The id is the profile's id; a profile without a choice has none.
/// </summary>
public sealed class PlaybackPreference : AggregateRoot
{
    private PlaybackPreference(Guid profileId, Guid userId)
        : base(profileId)
    {
        UserId = userId;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private PlaybackPreference()
    {
    }

    /// <summary>Whose profile it is: what an account deletion removes.</summary>
    public Guid UserId { get; private set; }

    /// <summary>A tag of <see cref="MediaLanguages"/>, or <see langword="null"/> for the video's original sound.</summary>
    public string? AudioLanguage { get; private set; }

    /// <summary>A tag of <see cref="MediaLanguages"/>, or <see langword="null"/> for subtitles off.</summary>
    public string? SubtitleLanguage { get; private set; }

    public static PlaybackPreference Create(Guid profileId, Guid userId)
    {
        Ensure.NotEmpty(profileId);
        Ensure.NotEmpty(userId);
        return new PlaybackPreference(profileId, userId);
    }

    public Result Choose(string? audioLanguage, string? subtitleLanguage)
    {
        if ((audioLanguage is not null && !MediaLanguages.IsSupported(audioLanguage)) || (subtitleLanguage is not null && !MediaLanguages.IsSupported(subtitleLanguage)))
        {
            return VideoErrors.LanguageUnsupported;
        }

        AudioLanguage = audioLanguage;
        SubtitleLanguage = subtitleLanguage;
        return Result.Success();
    }
}

public interface IPlaybackPreferenceRepository
{
    Task<PlaybackPreference?> GetAsync(Guid profileId, CancellationToken cancellationToken);

    void Add(PlaybackPreference preference);

    /// <summary>Removes, with the change being saved, the choice of a profile that no longer exists.</summary>
    Task RemoveForProfileAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Removes, with the change being saved, every choice of a user whose account was deleted.</summary>
    Task RemoveForUserAsync(Guid userId, CancellationToken cancellationToken);
}
