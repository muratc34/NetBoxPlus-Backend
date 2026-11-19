using Auth.Domain.RefreshTokens;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Microsoft.Extensions.Logging;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Sessions.Refresh;

public sealed record RefreshCommand(string RefreshToken) : ICommand<TokenPair>;

internal sealed class RefreshValidator : IValidator<RefreshCommand>
{
    /// <summary>Issued values are 43 characters; anything far longer is not one of ours.</summary>
    private const int MaxLength = 128;

    public IEnumerable<ValidationFailure> Validate(RefreshCommand instance)
    {
        if (string.IsNullOrEmpty(instance.RefreshToken) || instance.RefreshToken.Length > MaxLength)
        {
            yield return new ValidationFailure(nameof(instance.RefreshToken), RefreshTokenErrors.Invalid.Code, RefreshTokenErrors.Invalid.Message);
        }
    }
}

/// <summary>
/// Uses the refresh token once and issues the next pair with the user's current roles and
/// permissions. A token presented a second time was copied: its whole family is revoked, so
/// neither the thief nor the owner can go on with it. The revocations run at once (see
/// <see cref="IRefreshTokenRepository"/>), although the command fails.
/// </summary>
internal sealed partial class RefreshHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    SessionTokens sessions,
    TimeProvider timeProvider,
    ILogger<RefreshHandler> logger) : ICommandHandler<RefreshCommand, TokenPair>
{
    public async Task<Result<TokenPair>> Handle(RefreshCommand command, CancellationToken cancellationToken)
    {
        var token = await refreshTokens.GetByHashAsync(SessionTokens.Hash(command.RefreshToken), cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return RefreshTokenErrors.Invalid;
        }

        var now = timeProvider.GetUtcNow();

        if (token.IsSpent)
        {
            LogReuse(logger, token.FamilyId);
            return await EndAsync(token, now, cancellationToken).ConfigureAwait(false);
        }

        if (!token.IsActive(now))
        {
            return RefreshTokenErrors.Invalid;
        }

        // Losing this race means another request used the same token a moment ago: reuse as well.
        // The gateway refreshes once per session, so a legitimate client does not get here.
        if (!await refreshTokens.TryMarkUsedAsync(token.Id, now, cancellationToken).ConfigureAwait(false))
        {
            LogReuse(logger, token.FamilyId);
            return await EndAsync(token, now, cancellationToken).ConfigureAwait(false);
        }

        var user = await users.GetByIdAsync(token.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            return await EndAsync(token, now, cancellationToken).ConfigureAwait(false);
        }

        // An admin session ends as soon as the account loses the right to open one.
        var grants = await sessions.GetGrantsAsync(user, cancellationToken).ConfigureAwait(false);
        if (token.Kind == SessionKind.Admin && !grants.Has(Permissions.Auth.AdminSignIn))
        {
            return await EndAsync(token, now, cancellationToken).ConfigureAwait(false);
        }

        return sessions.Continue(user, token, grants);
    }

    private async Task<Result<TokenPair>> EndAsync(RefreshToken token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeFamilyAsync(token.FamilyId, now, cancellationToken).ConfigureAwait(false);
        return RefreshTokenErrors.Invalid;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reused; revoking token family {FamilyId}")]
    private static partial void LogReuse(ILogger logger, Guid familyId);
}
