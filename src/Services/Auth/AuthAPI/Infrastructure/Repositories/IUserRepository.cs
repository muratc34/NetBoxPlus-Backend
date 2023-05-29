using AuthAPI.Model;
using Shared;
using System.Linq.Expressions;

namespace AuthAPI.Infrastructure.Repositories
{
    public interface IUserRepository: IRepository<User>
    {
        public Task<List<OperationClaim>> GetClaims(User user);
        Task<List<User>> GetAllIncludeAsync(Expression<Func<User, bool>> filter = null!);
        Task<User> GetIncludeData(Expression<Func<User, bool>> filter);

    }
}
