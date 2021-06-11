using Microsoft.EntityFrameworkCore;
using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Repository.Base
{
    public abstract class RepositoryBase<TEntity, TId> : IRepositoryBase<TEntity, TId> where TEntity : class
    {
        protected RepositoryContext RepositoryContext { get; set; }

        public RepositoryBase(RepositoryContext repositoryContext)
        {
            RepositoryContext = repositoryContext;
        }

        /// <inheritdoc/>
        public async virtual Task<bool> Create(TEntity entity)
        {
            await RepositoryContext.Set<TEntity>().AddAsync(entity);
            return true;
        }

        /// <inheritdoc/>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public async virtual Task<bool> Delete(TEntity entity)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            RepositoryContext.Set<TEntity>().Remove(entity);
            return true;
        }

        /// <inheritdoc/>
        public async virtual Task<IEnumerable<TEntity>> GetAll() => await RepositoryContext.Set<TEntity>().AsNoTracking().ToListAsync();

        /// <inheritdoc/>
        public virtual IQueryable<TEntity> GetAllQueryable() => RepositoryContext.Set<TEntity>().AsNoTracking();

        /// <inheritdoc/>
        public async virtual Task<IEnumerable<TEntity>> FindAllByCondition(Expression<Func<TEntity, bool>> expression) => await RepositoryContext.Set<TEntity>().Where(expression).AsNoTracking().ToListAsync();

        /// <inheritdoc/>
        public virtual IQueryable<TEntity> FindAllByConditionQueryable(Expression<Func<TEntity, bool>> expression) => RepositoryContext.Set<TEntity>().Where(expression).AsNoTracking();

        /// <inheritdoc/>
        public async virtual Task<TEntity> FindFirstByCondition(Expression<Func<TEntity, bool>> expression) => await RepositoryContext.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(expression);

        /// <inheritdoc/>
        public async virtual Task<TEntity> FindById(TId id) => await RepositoryContext.Set<TEntity>().FindAsync(id);

        /// <inheritdoc/>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public async virtual Task<bool> Update(TEntity entity)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            RepositoryContext.Set<TEntity>().Update(entity);
            return true;
        }

        /// <inheritdoc/>
        public async virtual Task<bool> Exists(TId id) => await RepositoryContext.Set<TEntity>().OfType<IBase<TId>>().AnyAsync(entity => entity.Id.Equals(id));

        /// <inheritdoc/>
        public virtual void SetState(TEntity entity, EntityState entityState = EntityState.Modified) => RepositoryContext.Entry(entity).State = entityState;
    }
}
