using AuthAPI.Model;
using Microsoft.EntityFrameworkCore;
using Shared.Repository;

namespace AuthAPI.Infrastructure.Repositories
{
    public class UserRepository : RepositoryBase<User, AuthContext>, IUserRepository
    {
        public async Task<List<OperationClaim>> GetClaims(User user)
        {
            using (var context = new AuthContext())
            {
                var result = from operationClaim in context.OperationClaims
                             join userOperationClaim in context.UserOperationClaims
                                 on operationClaim.Id equals userOperationClaim.OperationClaimId
                             where userOperationClaim.UserId == user.Id
                             select new OperationClaim { Id = operationClaim.Id, Name = operationClaim.Name };
                return await result.ToListAsync();

            }
        }
    }
}
