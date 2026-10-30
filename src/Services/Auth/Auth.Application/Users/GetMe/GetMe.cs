using Auth.Application.Abstractions;
using Auth.Application.Sessions;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Users.GetMe;

public sealed record GetMeQuery(Guid UserId) : IQuery<MeResponse>;

/// <summary>The caller's account as stored now, which can be newer than the claims in their access token.</summary>
public sealed record MeResponse(Guid Id, string Email, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

internal sealed class GetMeHandler(IAuthReadModel readModel, SessionTokens sessions) : IQueryHandler<GetMeQuery, MeResponse>
{
    public async Task<Result<MeResponse>> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        var user = await readModel.Users
            .Include(user => user.Roles)
            .FirstOrDefaultAsync(user => user.Id == query.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var grants = await sessions.GetGrantsAsync(user, cancellationToken).ConfigureAwait(false);
        return new MeResponse(user.Id, user.Email.Value, grants.Roles, grants.Permissions);
    }
}
