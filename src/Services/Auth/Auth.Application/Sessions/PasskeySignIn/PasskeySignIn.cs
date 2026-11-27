using System.Text.Json;
using Auth.Application.Abstractions;
using Auth.Application.Passkeys;
using Auth.Domain.Passkeys;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Sessions.PasskeySignIn;

/// <summary>Options for signing in with a passkey; anyone may ask, the authenticator says whose it is.</summary>
public sealed record StartPasskeySignInCommand : ICommand<PasskeyCeremonyStart>;

/// <param name="Credential">The browser's <c>PublicKeyCredential</c> as JSON.</param>
/// <param name="Kind">An admin session also requires <see cref="Permissions.Auth.AdminSignIn"/>.</param>
public sealed record FinishPasskeySignInCommand(Guid ChallengeId, string Credential, SessionKind Kind, string? DeviceLabel = null) : ICommand<TokenPair>;

internal sealed class StartPasskeySignInHandler(IPasskeyRepository passkeys, IPasskeyCeremonies ceremonies, TimeProvider timeProvider)
    : ICommandHandler<StartPasskeySignInCommand, PasskeyCeremonyStart>
{
    public Task<Result<PasskeyCeremonyStart>> Handle(StartPasskeySignInCommand command, CancellationToken cancellationToken)
    {
        var options = ceremonies.SignInOptions();
        var challenge = PasskeyChallenge.ForSignIn(options, timeProvider.GetUtcNow());
        passkeys.AddChallenge(challenge);
        return Task.FromResult<Result<PasskeyCeremonyStart>>(new PasskeyCeremonyStart(challenge.Id, JsonDocument.Parse(options).RootElement.Clone()));
    }
}

internal sealed class FinishPasskeySignInValidator : IValidator<FinishPasskeySignInCommand>
{
    public IEnumerable<ValidationFailure> Validate(FinishPasskeySignInCommand instance)
    {
        if (!PasskeyLimits.IsCredentialSized(instance.Credential))
        {
            yield return new ValidationFailure(nameof(instance.Credential), PasskeyErrors.SignInFailed.Code, PasskeyErrors.SignInFailed.Message);
        }
    }
}

/// <summary>
/// Signs in the user whose passkey signed the challenge. Every failure answers the same
/// <see cref="PasskeyErrors.SignInFailed"/>: an unknown key, a wrong signature, a disabled account and a
/// missing admin permission look alike. A password lockout does not stop it: the lockout slows password
/// guessing, and a passkey cannot be guessed. A successful sign-in clears the failed password attempts.
/// </summary>
internal sealed class FinishPasskeySignInHandler(
    IPasskeyRepository passkeys,
    IPasskeyCeremonies ceremonies,
    IUserRepository users,
    SessionTokens sessions,
    TimeProvider timeProvider) : ICommandHandler<FinishPasskeySignInCommand, TokenPair>
{
    public async Task<Result<TokenPair>> Handle(FinishPasskeySignInCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var challenge = await passkeys.TryUseChallengeAsync(command.ChallengeId, PasskeyCeremony.SignIn, null, now, cancellationToken).ConfigureAwait(false);
        if (challenge is null || ceremonies.CredentialIdOf(command.Credential) is not { } credentialId)
        {
            return PasskeyErrors.SignInFailed;
        }

        var passkey = await passkeys.GetByCredentialIdAsync(credentialId, cancellationToken).ConfigureAwait(false);
        if (passkey is null)
        {
            return PasskeyErrors.SignInFailed;
        }

        var signCount = await ceremonies.VerifySignInAsync(
            challenge.Options,
            command.Credential,
            new StoredPasskey(passkey.CredentialId, passkey.PublicKey, passkey.SignCount),
            passkey.UserId,
            cancellationToken).ConfigureAwait(false);
        if (signCount is null)
        {
            return PasskeyErrors.SignInFailed;
        }

        var user = await users.GetByIdAsync(passkey.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            return PasskeyErrors.SignInFailed;
        }

        var grants = await sessions.GetGrantsAsync(user, cancellationToken).ConfigureAwait(false);
        if (command.Kind == SessionKind.Admin && !grants.Has(Permissions.Auth.AdminSignIn))
        {
            return PasskeyErrors.SignInFailed;
        }

        passkey.RecordUse(signCount.Value, now);
        user.RecordSuccessfulSignIn();
        return sessions.SignIn(user, command.Kind, grants, command.DeviceLabel);
    }
}
