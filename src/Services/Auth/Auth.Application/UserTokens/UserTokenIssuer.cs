using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Auth.Contracts;
using Auth.Domain.UserTokens;
using Shared.Application.Events;

namespace Auth.Application.UserTokens;

/// <summary>
/// Issues the tokens e-mailed to users. A token is added to the repository, so it is stored with the
/// command's save; its value leaves only through the integration event published for it.
/// </summary>
internal sealed class UserTokenIssuer(IUserTokenRepository tokens, TimeProvider timeProvider)
{
    /// <summary>256 random bits, the same strength as the SHA-256 hash that is stored.</summary>
    private const int ValueLength = 32;

    /// <summary>Far above a real token (43 characters); longer input is not worth hashing.</summary>
    public const int MaxValueLength = 256;

    /// <summary>The SHA-256 hash under which a token value is stored and looked up.</summary>
    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public UserToken IssueEmailVerification(Guid userId)
        => Add(UserToken.IssueEmailVerification(userId, NewValue(out var hash), hash, timeProvider.GetUtcNow()));

    public UserToken IssuePasswordReset(Guid userId)
        => Add(UserToken.IssuePasswordReset(userId, NewValue(out var hash), hash, timeProvider.GetUtcNow()));

    private static string NewValue(out byte[] hash)
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ValueLength));
        hash = Hash(value);
        return value;
    }

    private UserToken Add(UserToken token)
    {
        tokens.Add(token);
        return token;
    }
}

/// <summary>
/// Hands the token to the notification service. Runs while the token is being saved, so the outbox
/// message commits with it: a token that was never stored is never e-mailed.
/// </summary>
internal sealed class PublishEmailVerificationRequested(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<EmailVerificationTokenIssuedDomainEvent>
{
    public Task Handle(EmailVerificationTokenIssuedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new EmailVerificationRequested(domainEvent.UserId, domainEvent.TokenId, domainEvent.Token, domainEvent.ExpiresAt, timeProvider.GetUtcNow()),
            cancellationToken);
}

/// <inheritdoc cref="PublishEmailVerificationRequested"/>
internal sealed class PublishPasswordResetRequested(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<PasswordResetTokenIssuedDomainEvent>
{
    public Task Handle(PasswordResetTokenIssuedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new PasswordResetRequested(domainEvent.UserId, domainEvent.TokenId, domainEvent.Token, domainEvent.ExpiresAt, timeProvider.GetUtcNow()),
            cancellationToken);
}
