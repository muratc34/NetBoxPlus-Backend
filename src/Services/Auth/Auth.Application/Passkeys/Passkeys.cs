using System.Text.Json;
using Auth.Application.Abstractions;
using Auth.Domain.Passkeys;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Passkeys;

/// <summary>
/// Starts adding a passkey: the password is asked again (a stolen session alone must not add a way in), and
/// counts as a sign-in attempt when wrong.
/// </summary>
public sealed record StartPasskeyRegistrationCommand(Guid UserId, string Password) : ICommand<PasskeyCeremonyStart>;

/// <param name="Options">The <c>PublicKeyCredentialCreationOptions</c> or <c>…RequestOptions</c> JSON for the browser.</param>
public sealed record PasskeyCeremonyStart(Guid ChallengeId, JsonElement Options);

/// <param name="Credential">The browser's <c>PublicKeyCredential</c> as JSON (<c>toJSON()</c>).</param>
public sealed record FinishPasskeyRegistrationCommand(Guid UserId, Guid ChallengeId, string? Name, string Credential) : ICommand<Guid>;

public sealed record GetPasskeysQuery(Guid UserId) : IQuery<IReadOnlyList<PasskeySummary>>;

public sealed record PasskeySummary(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record RenamePasskeyCommand(Guid UserId, Guid PasskeyId, string? Name) : ICommand;

public sealed record RemovePasskeyCommand(Guid UserId, Guid PasskeyId) : ICommand;

internal static class PasskeyLimits
{
    /// <summary>More than any real credential answer; refused before it is parsed.</summary>
    public const int MaxCredentialLength = 64 * 1024;

    public static bool IsCredentialSized(string? credential) => credential is { Length: > 0 and <= MaxCredentialLength };
}

internal sealed class StartPasskeyRegistrationValidator : IValidator<StartPasskeyRegistrationCommand>
{
    public IEnumerable<ValidationFailure> Validate(StartPasskeyRegistrationCommand instance)
    {
        if (string.IsNullOrEmpty(instance.Password) || instance.Password.Length > PasswordPolicy.MaxLength)
        {
            yield return new ValidationFailure(nameof(instance.Password), UserErrors.PasswordRequired.Code, UserErrors.PasswordRequired.Message);
        }
    }
}

internal sealed class StartPasskeyRegistrationHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IPasskeyRepository passkeys,
    IPasskeyCeremonies ceremonies,
    TimeProvider timeProvider) : ICommandHandler<StartPasskeyRegistrationCommand, PasskeyCeremonyStart>
{
    public async Task<Result<PasskeyCeremonyStart>> Handle(StartPasskeyRegistrationCommand command, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var check = hasher.Verify(user.PasswordHash, command.Password);
        if (user.CanSignIn(now).IsFailure)
        {
            return UserErrors.PasswordIncorrect;
        }

        if (check == PasswordCheck.Failed)
        {
            await users.RecordFailedSignInAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
            return UserErrors.PasswordIncorrect;
        }

        if (await passkeys.CountAsync(user.Id, cancellationToken).ConfigureAwait(false) >= UserPasskey.MaxPerUser)
        {
            return PasskeyErrors.TooMany;
        }

        var existing = await passkeys.CredentialIdsAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var options = ceremonies.RegistrationOptions(user.Id, user.Email.Value, existing);
        var challenge = PasskeyChallenge.ForRegistration(user.Id, options, now);
        passkeys.AddChallenge(challenge);

        return new PasskeyCeremonyStart(challenge.Id, JsonDocument.Parse(options).RootElement.Clone());
    }
}

internal sealed class FinishPasskeyRegistrationValidator : IValidator<FinishPasskeyRegistrationCommand>
{
    public IEnumerable<ValidationFailure> Validate(FinishPasskeyRegistrationCommand instance)
    {
        if (UserPasskey.CleanName(instance.Name) is null)
        {
            yield return new ValidationFailure(nameof(instance.Name), PasskeyErrors.NameInvalid.Code, PasskeyErrors.NameInvalid.Message);
        }

        if (!PasskeyLimits.IsCredentialSized(instance.Credential))
        {
            yield return new ValidationFailure(nameof(instance.Credential), PasskeyErrors.Invalid.Code, PasskeyErrors.Invalid.Message);
        }
    }
}

/// <summary>
/// Checks the authenticator's answer against the options of the challenge, which is used up by trying. Two passkeys
/// with the same credential id are settled by the unique index (409 <c>Data.Conflict</c>).
/// </summary>
internal sealed class FinishPasskeyRegistrationHandler(
    IPasskeyRepository passkeys,
    IPasskeyCeremonies ceremonies,
    TimeProvider timeProvider) : ICommandHandler<FinishPasskeyRegistrationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(FinishPasskeyRegistrationCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var challenge = await passkeys.TryUseChallengeAsync(command.ChallengeId, PasskeyCeremony.Registration, command.UserId, now, cancellationToken).ConfigureAwait(false);
        if (challenge is null)
        {
            return PasskeyErrors.ChallengeInvalid;
        }

        var credential = await ceremonies.VerifyRegistrationAsync(challenge.Options, command.Credential, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return PasskeyErrors.Invalid;
        }

        if (await passkeys.GetByCredentialIdAsync(credential.CredentialId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return PasskeyErrors.AlreadyRegistered;
        }

        if (await passkeys.CountAsync(command.UserId, cancellationToken).ConfigureAwait(false) >= UserPasskey.MaxPerUser)
        {
            return PasskeyErrors.TooMany;
        }

        var passkey = UserPasskey.Register(command.UserId, credential.CredentialId, credential.PublicKey, credential.SignCount, command.Name!, now);
        if (passkey.IsFailure)
        {
            return passkey.Error;
        }

        passkeys.Add(passkey.Value);
        return passkey.Value.Id;
    }
}

internal sealed class GetPasskeysHandler(IAuthReadModel readModel) : IQueryHandler<GetPasskeysQuery, IReadOnlyList<PasskeySummary>>
{
    public async Task<Result<IReadOnlyList<PasskeySummary>>> Handle(GetPasskeysQuery query, CancellationToken cancellationToken)
        => await readModel.Passkeys
            .Where(passkey => passkey.UserId == query.UserId)
            .OrderBy(passkey => passkey.CreatedAt)
            .Select(passkey => new PasskeySummary(passkey.Id, passkey.Name, passkey.CreatedAt, passkey.LastUsedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class RenamePasskeyValidator : IValidator<RenamePasskeyCommand>
{
    public IEnumerable<ValidationFailure> Validate(RenamePasskeyCommand instance)
    {
        if (UserPasskey.CleanName(instance.Name) is null)
        {
            yield return new ValidationFailure(nameof(instance.Name), PasskeyErrors.NameInvalid.Code, PasskeyErrors.NameInvalid.Message);
        }
    }
}

internal sealed class RenamePasskeyHandler(IPasskeyRepository passkeys) : ICommandHandler<RenamePasskeyCommand>
{
    public async Task<Result> Handle(RenamePasskeyCommand command, CancellationToken cancellationToken)
        => await passkeys.GetAsync(command.UserId, command.PasskeyId, cancellationToken).ConfigureAwait(false) is { } passkey
            ? passkey.Rename(command.Name!)
            : PasskeyErrors.NotFound;
}

/// <summary>Removing a passkey asks for no password: it takes a way in away, and the user is told by e-mail.</summary>
internal sealed class RemovePasskeyHandler(IPasskeyRepository passkeys, TimeProvider timeProvider) : ICommandHandler<RemovePasskeyCommand>
{
    public async Task<Result> Handle(RemovePasskeyCommand command, CancellationToken cancellationToken)
    {
        if (await passkeys.GetAsync(command.UserId, command.PasskeyId, cancellationToken).ConfigureAwait(false) is not { } passkey)
        {
            return PasskeyErrors.NotFound;
        }

        passkey.Remove(timeProvider.GetUtcNow());
        passkeys.Remove(passkey);
        return Result.Success();
    }
}
