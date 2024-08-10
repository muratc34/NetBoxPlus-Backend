using Auth.Domain.Users;
using Persistence.Context;
using Shared.Repository;

namespace Persistence.Repositories;

public sealed class UserRepository : Repository<User,DatabaseContext>, IUserRepository
{
    public UserRepository(DatabaseContext context)
        : base(context)
    {
    }
}
