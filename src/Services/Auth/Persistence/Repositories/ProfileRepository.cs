using Auth.Domain.Profiles;
using Persistence.Context;
using Shared.Repository;

namespace Auth.Persistence.Repositories;

public class ProfileRepository : Repository<Profile, DatabaseContext>, IProfileRepository
{
    public ProfileRepository(DatabaseContext context)
        : base(context)
    {
        
    }
}
