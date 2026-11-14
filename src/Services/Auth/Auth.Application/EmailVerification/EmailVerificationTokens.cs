using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Auth.Contracts;
using Auth.Domain.UserTokens;
using Shared.Application.Events;

namespace Auth.Application.EmailVerification;

/// <summary>
/// Issues e-mail verification tokens. The token is added to the repository, so it is stored with the
/// command's save; its value leaves only through <see cref="EmailVerificationRequested"/>.
/// </summary>
internal sealed class EmailVerificationTokens(IUserTokenRepository tokens, TimeProvider timeProvider)
{
    /// <summary>256 random bits, the same strength as the SHA-256 hash that is stored.</summary>
    private const int ValueLength = 32;

    /// <summary>The SHA-256 hash under which a token value is stored and looked up.</summary>
    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public UserToken Issue(Guid userId)
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ValueLength));
        var token = UserToken.IssueEmailVerification(userId, value, Hash(value), timeProvider.GetUtcNow());
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
