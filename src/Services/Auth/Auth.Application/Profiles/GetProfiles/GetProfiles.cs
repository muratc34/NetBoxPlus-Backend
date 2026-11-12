using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Profiles.GetProfiles;

/// <summary>The caller's profiles, oldest first.</summary>
public sealed record GetProfilesQuery(Guid UserId) : IQuery<IReadOnlyList<ProfileResponse>>;

public sealed record ProfileResponse(Guid Id, string Name, AgeRating MaxAgeRating);

internal sealed class GetProfilesHandler(IAuthReadModel readModel) : IQueryHandler<GetProfilesQuery, IReadOnlyList<ProfileResponse>>
{
    public async Task<Result<IReadOnlyList<ProfileResponse>>> Handle(GetProfilesQuery query, CancellationToken cancellationToken)
    {
        var profiles = await readModel.Users
            .Where(user => user.Id == query.UserId)
            .SelectMany(user => user.Profiles)
            .OrderBy(profile => profile.Id)
            .Select(profile => new ProfileResponse(profile.Id, profile.Name, profile.MaxAgeRating))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return profiles;
    }
}
