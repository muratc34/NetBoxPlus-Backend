using AuthAPI.Model;
using Shared;

namespace AuthAPI.Infrastructure.Repositories
{
    public interface IUserRepository: IRepository<User>
    {
        public Task<List<OperationClaim>> GetClaims(User user);
        
    }
}
