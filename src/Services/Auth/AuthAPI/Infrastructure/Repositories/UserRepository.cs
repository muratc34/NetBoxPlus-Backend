using AuthAPI.Model;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Repository;
using System.Linq;
using System.Linq.Expressions;

namespace AuthAPI.Infrastructure.Repositories
{
    public class UserRepository : RepositoryBase<User, AuthContext>, IUserRepository
    {
        public async Task<List<OperationClaim>> GetClaims(User user)
        {
            using (var context = new AuthContext())
            {
                var result = from operationClaim in context.OperationClaims
                             join operationClaimUser in context.OperationClaimsUser
                                 on operationClaim.Id equals operationClaimUser.OperationClaimId
                             where operationClaimUser.UserId == user.Id
                             select new OperationClaim { Id = operationClaim.Id, Name = operationClaim.Name };
                return await result.ToListAsync();

            }
        }

        public async Task<User> GetIncludeData(Expression<Func<User, bool>> filter)
        {
            using (AuthContext context = new AuthContext())
            {
                return (await context.Set<User>().Include("Profiles").SingleOrDefaultAsync(filter))!;
            }
        }

        public async Task<List<User>> GetAllIncludeAsync(Expression<Func<User, bool>> filter = null!)
        {
            using (AuthContext context = new AuthContext())
            {
                return await (filter == null ? context.Set<User>().Include("Profiles").ToListAsync() : context.Set<User>().Include("Subscriptions").Include("Profiles").Where(filter).ToListAsync());
            }
        }
    }
}
