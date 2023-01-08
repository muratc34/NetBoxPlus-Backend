using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public interface IRepository<T> where T : class, IEntity, new()
    {
        Task CreateAsync(T entity);
        Task<List<T>> GetAllAsync(Expression<Func<T, bool>> filter = null!);
        Task<T> GetAsync(Expression<Func<T, bool>> filter);
        Task RemoveAsync(T entity);
        Task UpdateAsync(T entity);
    }
}
