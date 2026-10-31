using Auth.Application.Abstractions;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Shared.Application.Auditing;
using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.Application.Users.SeedAdministrator;

/// <summary>
/// Run by the migrator on every deploy. Without an e-mail address and password only the
/// administrator role is kept up to date.
/// </summary>
/// <remarks>
/// Not an <see cref="IAuditedCommand"/>, which would record the password; the handler writes its
/// own audit entries without it.
/// </remarks>
public sealed record SeedAdministratorCommand(string? Email, string? Password) : ICommand<SeedAdministratorOutcome>;

/// <param name="RoleChanged">The role was created or received new permissions from the catalog.</param>
/// <param name="UserCreated">An account was created for the e-mail address.</param>
/// <param name="RoleAssigned">The account did not have the role and got it.</param>
public sealed record SeedAdministratorOutcome(bool RoleChanged, bool UserCreated, bool RoleAssigned);

internal sealed class SeedAdministratorValidator : IValidator<SeedAdministratorCommand>
{
    public IEnumerable<ValidationFailure> Validate(SeedAdministratorCommand instance)
    {
        var hasEmail = !string.IsNullOrEmpty(instance.Email);
        var hasPassword = !string.IsNullOrEmpty(instance.Password);

        if (hasEmail != hasPassword)
        {
            yield return new ValidationFailure(
                hasEmail ? nameof(instance.Password) : nameof(instance.Email),
                "Seed.Incomplete",
                "The administrator e-mail address and password are set together or not at all.");
            yield break;
        }

        if (!hasEmail)
        {
            yield break;
        }

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
/// Makes sure the system administrator role exists and holds every permission in the catalog, and
/// that the configured account exists and has it. Idempotent; an existing account keeps its password
/// and state. Running it again is also the way back in when administrators have lost the role.
/// </summary>
internal sealed class SeedAdministratorHandler(
    IRoleRepository roles,
    IUserRepository users,
    IPasswordHasher hasher,
    IAuditLog auditLog) : ICommandHandler<SeedAdministratorCommand, SeedAdministratorOutcome>
{
    public async Task<Result<SeedAdministratorOutcome>> Handle(SeedAdministratorCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetSystemRoleAsync(cancellationToken).ConfigureAwait(false);
        bool roleChanged;

        if (role is null)
        {
            role = Role.CreateAdministrator();
            roles.Add(role);
            roleChanged = true;
        }
        else
        {
            roleChanged = role.SyncSystemPermissions();
        }

        if (roleChanged)
        {
            auditLog.Add(new AuditEntry("SeedAdministratorRole", null, role.Id, new { role.Permissions }));
        }

        if (string.IsNullOrEmpty(command.Email) || string.IsNullOrEmpty(command.Password))
        {
            return new SeedAdministratorOutcome(roleChanged, UserCreated: false, RoleAssigned: false);
        }

        var email = EmailAddress.Create(command.Email).Value;
        var user = await users.GetByEmailAsync(email, cancellationToken).ConfigureAwait(false);
        var userCreated = user is null;

        if (user is null)
        {
            user = User.Register(email, hasher.Hash(command.Password));
            users.Add(user);
        }

        var roleAssigned = !user.Roles.Any(userRole => userRole.RoleId == role.Id);
        if (roleAssigned)
        {
            user.AssignRole(role.Id);
            auditLog.Add(new AuditEntry("SeedAdministrator", null, user.Id, new { RoleId = role.Id, UserCreated = userCreated }));
        }

        return new SeedAdministratorOutcome(roleChanged, userCreated, roleAssigned);
    }
}
