using Auth.Domain.Roles;
using Auth.Domain.Users;

namespace Auth.Application.Abstractions;

/// <summary>Untracked queryable views for query handlers, which project straight to response types.</summary>
public interface IAuthReadModel
{
    IQueryable<User> Users { get; }

    IQueryable<Role> Roles { get; }
}
