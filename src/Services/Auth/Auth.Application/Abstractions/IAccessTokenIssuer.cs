namespace Auth.Application.Abstractions;

/// <summary>
/// Signs the short-lived access token that every service accepts. The token carries the user's
/// roles and permissions as they are at the time of issue; a change shows in the next token.
/// </summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(AccessTokenSubject subject);
}

/// <param name="Roles">Role names, one <c>role</c> claim each.</param>
/// <param name="Permissions">Granted permissions, one <c>permission</c> claim each.</param>
public sealed record AccessTokenSubject(
    Guid UserId,
    string Email,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
