using Microsoft.EntityFrameworkCore;
using Shared.Domain;
using Streaming.Application.Abstractions;
using Streaming.Domain.Access;
using Streaming.Domain.Playback;

namespace Streaming.Application.Playback;

/// <summary>
/// The profile a request acts for: one of the caller's, not removed, and if it is locked, opened in the caller's
/// sign-in (403 <see cref="PlaybackErrors.ProfileLocked"/>).
/// </summary>
internal sealed class ProfileAccess(IStreamingReadModel readModel, IUnlockedProfiles unlocked)
{
    public async Task<Result<ProfileCopy>> FindAsync(Guid userId, Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await readModel.Profiles
            .FirstOrDefaultAsync(item => item.Id == profileId && item.UserId == userId, cancellationToken).ConfigureAwait(false);
        return Check(profile);
    }

    public Result<ProfileCopy> Check(ProfileCopy? profile)
    {
        if (profile is null || profile.IsRemoved)
        {
            return PlaybackErrors.ProfileNotFound;
        }

        return profile.IsLocked && !unlocked.Contains(profile.Id) ? PlaybackErrors.ProfileLocked : profile;
    }
}
