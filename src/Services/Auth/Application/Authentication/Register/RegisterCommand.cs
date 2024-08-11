using Auth.Domain.Authentication;
using Shared.Application.Abstractions;
using Shared.Domain.Result;

namespace Auth.Application.Authentication.Register;

public sealed class RegisterCommand : ICommand<Result<AccessToken>>
{
    public RegisterCommand(string firstName, string lastName, string email, string password)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Password = password;
    }

    public string FirstName { get; }
    public string LastName { get; }
    public string Email { get; }
    public string Password { get; }
}
