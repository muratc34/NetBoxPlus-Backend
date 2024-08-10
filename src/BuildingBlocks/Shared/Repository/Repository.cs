using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Shared.Domain;
using Shared.Domain.Abstractions;
using System.Linq.Expressions;

namespace Shared.Repository;

public interface IRepository<TEntity>
    where TEntity : Entity, ISoftDeletableEntity, IAuditableEntity
{
    public Task CreateAsync(TEntity entity);
    public Task HardDeleteAsync(TEntity entity);
    public Task SoftDeleteAsync(TEntity entity);
    public Task<TEntity> UpdateAsync(TEntity entity);
    public Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate);
    public Task<List<TEntity>> GetAllAsync(Expression<Func<TEntity, bool>>? predicate = null, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, CancellationToken cancellationToken = default);
    public Task<IList<TEntity>> GetAllByPagingAsync(Expression<Func<TEntity, bool>>? predicate = null, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, bool enableTracking = false, int currentPage = 1, int pageSize = 10);
    public Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null);
    public IQueryable<TEntity> Find(Expression<Func<TEntity, bool>> predicate);
    public IQueryable<TEntity> FindAll();

}

public abstract class Repository<TEntity, TContext> : IRepository<TEntity>
    where TEntity : Entity, ISoftDeletableEntity, IAuditableEntity
    where TContext : DbContext
{
    private readonly TContext _context;

    public Repository(TContext context) => _context = context;

    public async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate)
    {
        _context.Set<TEntity>().AsNoTracking();
        if (predicate is null)
        {
            return await _context.Set<TEntity>().CountAsync();
        }
        return await _context.Set<TEntity>().Where(predicate).CountAsync();
    }

    public async Task CreateAsync(TEntity entity)
    {
        await _context.Set<TEntity>().AddAsync(entity);
    }

    public IQueryable<TEntity> Find(Expression<Func<TEntity, bool>> predicate) => _context.Set<TEntity>().AsNoTracking().Where(predicate);

    public IQueryable<TEntity> FindAll() => _context.Set<TEntity>().AsNoTracking();

    public async Task<List<TEntity>> GetAllAsync(Expression<Func<TEntity, bool>>? predicate = null, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> queryable = _context.Set<TEntity>().AsNoTracking();
        if (include is not null) queryable = include(queryable);
        if (predicate is not null) queryable = queryable.Where(predicate);
        return await queryable.ToListAsync(cancellationToken);
    }

    public async Task<IList<TEntity>> GetAllByPagingAsync(Expression<Func<TEntity, bool>>? predicate = null, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, bool enableTracking = false, int currentPage = 1, int pageSize = 10)
    {
        IQueryable<TEntity> queryable = _context.Set<TEntity>();
        if (!enableTracking) queryable = queryable.AsNoTracking();
        if (include is not null) queryable = include(queryable);
        if (predicate is not null) queryable = queryable.Where(predicate);
        if (orderBy is not null)
            return await orderBy(queryable).Skip((currentPage - 1) * pageSize).Take(pageSize).ToListAsync();

        return await queryable.Skip((currentPage - 1) * pageSize).Take(pageSize).ToListAsync();
    }

    public async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null)
    {
        IQueryable<TEntity> queryable = _context.Set<TEntity>().AsNoTracking();
        if (include is not null) queryable = include(queryable);
        return await queryable.FirstOrDefaultAsync(predicate);
    }

    public async Task HardDeleteAsync(TEntity entity)
    {
        await Task.Run(() => _context.Set<TEntity>().Remove(entity));
    }

    public async Task SoftDeleteAsync(TEntity entity)
    {
        await Task.Run(() => _context.Set<TEntity>()
            .Where(e => e.Id == entity.Id)
            .ExecuteUpdate(e => e
                .SetProperty(p => p.Deleted, true)
                .SetProperty(p => p.DeletedOnUtc, DateTime.UtcNow)));
    }

    public async Task<TEntity> UpdateAsync(TEntity entity)
    {
        await Task.Run(() => _context.Set<TEntity>().Update(entity));
        return entity;
    }
}
