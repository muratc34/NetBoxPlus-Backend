using Domain.Core.Primitives;

namespace Domain.Core.Errors
{
    public static class DomainErrors
    {
        public static Error NotFound => new Error("User.NotFound", "The user with the specified identifier was not found.");
        public static Error InvalidPermissions => new Error("User.InvalidPermissions", "The current user does not have the permissions to perform that operation.");
        public static Error DuplicateEmail => new Error("User.DuplicateEmail", "The specified email is already in use.");
        public static Error CannotChangePassword => new Error("User.CannotChangePassword", "The password cannot be changed to the specified password.");
    }

    public static class General
    {
        public static Error UnProcessableRequest => new Error("General.UnProcessableRequest", "The server could not process the request.");
        public static Error ServerError => new Error("General.ServerError", "The server encountered an unrecoverable error.");
    }

    public static class Authentication
    {
        public static Error InvalidEmailOrPassword => new Error("Authentication.InvalidEmailOrPassword", "The specified email or password are incorrect.");
    }
}
