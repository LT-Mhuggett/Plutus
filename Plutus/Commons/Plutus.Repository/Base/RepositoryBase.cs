using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Plutus.Repository.Base
{
    public abstract class RepositoryBase<TEntity, TId> : IRepositoryBase<TEntity, TId> where TEntity : Base<TId>
    {
        public RepositoryBase(RepositoryContext repositoryContext)
        {
            RepositoryContext = repositoryContext;
        }

        protected RepositoryContext RepositoryContext { get; init; }
        /// <inheritdoc/>
        public async virtual Task<bool> Create(TEntity entity, IDbContextTransaction dbTransaction = default)
        {
            await RepositoryContext.Set<TEntity>().AddAsync(entity);
            return true;
        }

        /// <inheritdoc/>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async virtual Task<bool> Delete(TEntity entity, IDbContextTransaction dbTransaction = default)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                RepositoryContext.Set<TEntity>().Remove(entity);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public async virtual Task<bool> Exists(TId id) => await RepositoryContext.Set<TEntity>().OfType<IBase<TId>>().AnyAsync(entity => entity.Id.Equals(id));

        /// <inheritdoc/>
        public async virtual Task<IEnumerable<TEntity>> FindAllByCondition(Expression<Func<TEntity, bool>> expression) => await RepositoryContext.Set<TEntity>().Where(expression).AsNoTracking().ToListAsync();

        /// <inheritdoc/>
        public virtual IQueryable<TEntity> FindAllByConditionQueryable(Expression<Func<TEntity, bool>> expression) => RepositoryContext.Set<TEntity>().Where(expression).AsNoTracking();

        /// <inheritdoc/>
        public async virtual Task<TEntity> FindById(TId id) => await RepositoryContext.Set<TEntity>().FindAsync(id);

        /// <inheritdoc/>
        public async virtual Task<TEntity> FindById(IQueryable<TEntity> query, TId id) => await query.FirstOrDefaultAsync(e => e.Id.Equals(id));

        /// <inheritdoc/>
        public async virtual Task<TEntity> FindFirstByCondition(Expression<Func<TEntity, bool>> expression) => await RepositoryContext.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(expression);

        /// <inheritdoc/>
        public async virtual Task<IEnumerable<TEntity>> GetAll() => await RepositoryContext.Set<TEntity>().AsNoTrackingWithIdentityResolution().ToListAsync();

        /// <inheritdoc/>
        public virtual IQueryable<TEntity> GetAllQueryable() => RepositoryContext.Set<TEntity>().AsNoTrackingWithIdentityResolution();

        /// <inheritdoc/>
        public virtual void SetState(TEntity entity, EntityState entityState = EntityState.Modified) => RepositoryContext.Entry(entity).State = entityState;

        /// <inheritdoc/>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async virtual Task<bool> Update(TEntity entity, IDbContextTransaction dbTransaction = default)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                RepositoryContext.Set<TEntity>().Update(entity);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
