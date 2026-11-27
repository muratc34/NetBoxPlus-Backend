using Auth.Application.Abstractions;
using Auth.Domain.RefreshTokens;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Sessions.Devices;

/// <summary>
/// The account's open sessions, one per sign-in (refresh token family) that can still be renewed, the most
/// recently active first.
/// </summary>
/// <param name="CurrentSessionId">The caller's own session (the access token's <c>sid</c>), marked in the list.</param>
public sealed record GetSessionsQuery(Guid UserId, Guid? CurrentSessionId) : IQuery<IReadOnlyList<DeviceSession>>;

/// <param name="Id">The session's id: the refresh token family.</param>
/// <param name="DeviceLabel">Such as "Chrome · Windows"; <see langword="null"/> when it is not known.</param>
/// <param name="LastActiveAt">When the session was last renewed, which a browser in use does every few minutes.</param>
/// <param name="Current">The session the request came with.</param>
public sealed record DeviceSession(Guid Id, SessionKind Kind, string? DeviceLabel, DateTimeOffset SignedInAt, DateTimeOffset LastActiveAt, bool Current);

/// <summary>Ends one of the account's sessions: it cannot be renewed any more.</summary>
public sealed record EndSessionCommand(Guid UserId, Guid SessionId) : ICommand;

/// <summary>Ends every session of the account but the caller's own.</summary>
public sealed record EndOtherSessionsCommand(Guid UserId, Guid? CurrentSessionId) : ICommand<EndedSessions>;

/// <param name="Ids">The sessions that ended, for the gateway to forget the browsers that used them.</param>
public sealed record EndedSessions(IReadOnlyList<Guid> Ids);

public static class SessionErrors
{
    public static readonly Error NotFound = Error.NotFound("Session.NotFound", "The session was not found, or it has ended already.");
}

/// <summary>The account's sessions that can still be renewed: each has one token neither used nor revoked nor expired.</summary>
internal static class LiveSessions
{
    public static IQueryable<RefreshToken> Of(IAuthReadModel readModel, Guid userId, DateTimeOffset now)
        => readModel.RefreshTokens.Where(token => token.UserId == userId && token.UsedAt == null && token.RevokedAt == null && token.ExpiresAt > now);
}

internal sealed class GetSessionsHandler(IAuthReadModel readModel, TimeProvider timeProvider) : IQueryHandler<GetSessionsQuery, IReadOnlyList<DeviceSession>>
{
    public async Task<Result<IReadOnlyList<DeviceSession>>> Handle(GetSessionsQuery query, CancellationToken cancellationToken)
    {
        var live = await LiveSessions.Of(readModel, query.UserId, timeProvider.GetUtcNow())
            .Select(token => new { token.FamilyId, token.Kind, token.DeviceLabel, token.CreatedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var families = live.Select(token => token.FamilyId).ToList();
        var signedIn = await readModel.RefreshTokens
            .Where(token => families.Contains(token.FamilyId))
            .GroupBy(token => token.FamilyId)
            .Select(group => new { FamilyId = group.Key, At = group.Min(token => token.CreatedAt) })
            .ToDictionaryAsync(family => family.FamilyId, family => family.At, cancellationToken).ConfigureAwait(false);

        return live
            .Select(token => new DeviceSession(
                token.FamilyId,
                token.Kind,
                token.DeviceLabel,
                signedIn.GetValueOrDefault(token.FamilyId, token.CreatedAt),
                token.CreatedAt,
                token.FamilyId == query.CurrentSessionId))
            .OrderByDescending(session => session.LastActiveAt)
            .ToList();
    }
}

/// <summary>
/// Another user's session, or one that ended already, is not found. The revocation runs at once
/// (<see cref="IRefreshTokenRepository.RevokeFamilyAsync"/>); the gateway forgets the browser's session after it.
/// </summary>
internal sealed class EndSessionHandler(IAuthReadModel readModel, IRefreshTokenRepository refreshTokens, TimeProvider timeProvider) : ICommandHandler<EndSessionCommand>
{
    public async Task<Result> Handle(EndSessionCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!await LiveSessions.Of(readModel, command.UserId, now).AnyAsync(token => token.FamilyId == command.SessionId, cancellationToken).ConfigureAwait(false))
        {
            return SessionErrors.NotFound;
        }

        await refreshTokens.RevokeFamilyAsync(command.SessionId, now, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>
/// Without a current session (a token issued before sessions had ids) every session ends, the caller's too: it
/// signs in again.
/// </summary>
internal sealed class EndOtherSessionsHandler(IAuthReadModel readModel, IRefreshTokenRepository refreshTokens, TimeProvider timeProvider)
    : ICommandHandler<EndOtherSessionsCommand, EndedSessions>
{
    public async Task<Result<EndedSessions>> Handle(EndOtherSessionsCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var others = await LiveSessions.Of(readModel, command.UserId, now)
            .Where(token => token.FamilyId != command.CurrentSessionId)
            .Select(token => token.FamilyId)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var familyId in others)
        {
            await refreshTokens.RevokeFamilyAsync(familyId, now, cancellationToken).ConfigureAwait(false);
        }

        return new EndedSessions(others);
    }
}
