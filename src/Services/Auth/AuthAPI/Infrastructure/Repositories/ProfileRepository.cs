using AuthAPI.Model;
using Shared;
using Shared.Repository;

namespace AuthAPI.Infrastructure.Repositories
{
    public class ProfileRepository : RepositoryBase<Profile, AuthContext>, IProfileRepository
    {
    }
}
