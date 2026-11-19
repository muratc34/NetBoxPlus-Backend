using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.EmailVerification.GetVerificationStatus;

/// <param name="UserId">The signed-in caller, from the access token.</param>
public sealed record GetVerificationStatusQuery(Guid UserId) : IQuery<VerificationStatus>;

/// <summary>
/// Whether the caller's address is verified. The browser reaches this through the gateway, which does
/// not expose <c>/me</c>.
/// </summary>
public sealed record VerificationStatus(bool Verified, DateTimeOffset? VerifiedAt);

internal sealed class GetVerificationStatusHandler(IAuthReadModel readModel) : IQueryHandler<GetVerificationStatusQuery, VerificationStatus>
{
    public async Task<Result<VerificationStatus>> Handle(GetVerificationStatusQuery query, CancellationToken cancellationToken)
    {
        var status = await readModel.Users
            .Where(user => user.Id == query.UserId)
            .Select(user => new { user.EmailVerifiedAt })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return status is null
            ? UserErrors.NotFound
            : new VerificationStatus(status.EmailVerifiedAt is not null, status.EmailVerifiedAt);
    }
}
