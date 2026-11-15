using Auth.Application.Abstractions;
using Auth.Contracts;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Events;
using Shared.Domain;

namespace Auth.Application.EmailVerification.AnnounceEmailVerifications;

/// <summary>
/// Publishes <see cref="EmailVerified"/> again for every verified account: fills a consumer that
/// started after the accounts were verified (billing, before it knew about verification). Consumers
/// handle it more than once without harm. Run by the migrator on request, not by people.
/// </summary>
public sealed record AnnounceEmailVerificationsCommand : ICommand<int>;

internal sealed class AnnounceEmailVerificationsHandler(IAuthReadModel readModel, IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : ICommandHandler<AnnounceEmailVerificationsCommand, int>
{
    public async Task<Result<int>> Handle(AnnounceEmailVerificationsCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var verified = await readModel.Users
            .Where(user => user.EmailVerifiedAt != null)
            .Select(user => new { user.Id, user.Email, VerifiedAt = user.EmailVerifiedAt!.Value })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var user in verified)
        {
            await publisher.PublishAsync(new EmailVerified(user.Id, user.Email.Value, user.VerifiedAt, now, Replayed: true), cancellationToken).ConfigureAwait(false);
        }

        return verified.Count;
    }
}
