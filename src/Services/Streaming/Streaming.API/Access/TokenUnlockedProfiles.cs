using Streaming.Application.Abstractions;

namespace Streaming.API.Access;

/// <summary>The profiles named in the access token's <c>profile_unlocked</c> claims, which the auth service adds for this sign-in.</summary>
internal sealed class TokenUnlockedProfiles(IHttpContextAccessor httpContextAccessor) : IUnlockedProfiles
{
    public const string ClaimType = "profile_unlocked";

    public bool Contains(Guid profileId)
        => httpContextAccessor.HttpContext?.User.HasClaim(ClaimType, profileId.ToString()) == true;
}
