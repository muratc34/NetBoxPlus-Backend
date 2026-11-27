using Auth.Application.Abstractions;
using Auth.Application.Sessions;
using Auth.Application.UserTokens;
using Auth.Domain.RefreshTokens;
using Auth.Domain.Users;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Users.Register;

/// <param name="DeviceLabel">What the sign-up came from, for the first session in the list of devices.</param>
public sealed record RegisterCommand(string Email, string Password, string? DeviceLabel = null) : ICommand<RegisteredAccount>;

/// <summary>The new account and its first customer session.</summary>
public sealed record RegisteredAccount(Guid UserId, TokenPair Tokens);

internal sealed class RegisterValidator : IValidator<RegisterCommand>
{
    public IEnumerable<ValidationFailure> Validate(RegisterCommand instance)
    {
        var email = EmailAddress.Create(instance.Email);
        if (email.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.Email), email.Error.Code, email.Error.Message);
        }

        var password = PasswordPolicy.Check(instance.Password);
        if (password.IsFailure)
        {
            yield return new ValidationFailure(nameof(instance.Password), password.Error.Code, password.Error.Message);
        }
    }
}

/// <summary>
/// Creates the account, its first e-mail verification token and its first customer session, all in
/// one transaction. The session comes from here rather than from a sign-in afterwards: that would
/// verify the password just hashed, a second deliberately expensive step, and could fail after the
/// account already exists. A taken address is reported (409): verification is not required to sign
/// in, so it cannot be hidden, and the gateway rate-limits this route.
/// </summary>
internal sealed class RegisterHandler(IUserRepository users, IPasswordHasher hasher, UserTokenIssuer tokenIssuer, SessionTokens sessions)
    : ICommandHandler<RegisterCommand, RegisteredAccount>
{
    public async Task<Result<RegisteredAccount>> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(command.Email).Value;

        if (await users.ExistsByEmailAsync(email, cancellationToken).ConfigureAwait(false))
        {
            return UserErrors.EmailTaken;
        }

        var user = User.Register(email, hasher.Hash(command.Password));
        users.Add(user);
        tokenIssuer.IssueEmailVerification(user.Id);

        var grants = await sessions.GetGrantsAsync(user, cancellationToken).ConfigureAwait(false);
        return new RegisteredAccount(user.Id, sessions.Start(user, SessionKind.Customer, grants, command.DeviceLabel));
    }
}
