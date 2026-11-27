using Auth.Contracts;
using Auth.Domain.Passkeys;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Users;
using Auth.Domain.UserTokens;
using Shared.Application.Events;

namespace Auth.Application.Users.DeleteAccount;

/// <summary>What deleting an account does, whoever asks for it.</summary>
internal sealed class AccountDeletion(IRefreshTokenRepository refreshTokens, IUserTokenRepository tokens, IPasskeyRepository passkeys, TimeProvider timeProvider)
{
    private static readonly UserTokenPurpose[] Purposes = Enum.GetValues<UserTokenPurpose>();

    /// <summary>
    /// Anonymizes the account and ends its sessions at once (access tokens already issued expire on
    /// their own; the services refuse a deleted user from their copies). Its e-mailed links stop working
    /// with the save, and its passkeys are deleted.
    /// </summary>
    public async Task DeleteAsync(User user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        user.Delete(now);

        foreach (var purpose in Purposes)
        {
            foreach (var token in await tokens.GetUnusedAsync(user.Id, purpose, cancellationToken).ConfigureAwait(false))
            {
                token.Revoke(now);
            }
        }

        await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
        await passkeys.RemoveForUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Tells every service to delete its copy of the user. Runs while the deletion is being saved, so a
/// deletion that failed is never announced.
/// </summary>
internal sealed class PublishUserDeleted(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<UserDeletedDomainEvent>
{
    public Task Handle(UserDeletedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(new UserDeleted(domainEvent.UserId, domainEvent.DeletedAt, timeProvider.GetUtcNow()), cancellationToken);
}
