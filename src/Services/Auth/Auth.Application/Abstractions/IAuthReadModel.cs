using Auth.Domain.Roles;
using Auth.Domain.Users;

namespace Auth.Application.Abstractions;

/// <summary>Untracked queryable views for query handlers, which project straight to response types.</summary>
public interface IAuthReadModel
{
    IQueryable<User> Users { get; }

    IQueryable<Role> Roles { get; }

    /// <summary>
    /// Users whose e-mail address contains <paramref name="fragment"/>, ignoring case; the
    /// fragment is matched literally, so <c>%</c> and <c>_</c> are not wildcards.
    /// </summary>
    IQueryable<User> UsersWithEmailContaining(string fragment);
}
