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
using System.Text;
using System.Threading.Tasks;
 
namespace Plutus.Repository.Base
{
    public abstract class CompositeRepositoryBase<TEntity, TIdOne, TIdTwo> : ICompositeRepositoryBase<TEntity, TIdOne, TIdTwo> where TEntity : class
    {
        protected RepositoryContext RepositoryContext { get; set; }

        public CompositeRepositoryBase(RepositoryContext repositoryContext)
        {
            RepositoryContext = repositoryContext;
        }

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
            RepositoryContext.Set<TEntity>().Remove(entity);
            return true;
        }

        /// <inheritdoc/>
        public async virtual Task<IEnumerable<TEntity>> GetAll(TIdTwo businessId) => await RepositoryContext.Set<TEntity>().AsNoTracking().Where(r => (r as ICompositeBase<TIdOne, TIdTwo>).IdTwo.Equals(businessId)).ToListAsync();

        /// <inheritdoc/>
        public virtual async Task<IQueryable<TEntity>> GetAllQueryable(TIdTwo businessId) => RepositoryContext.Set<TEntity>().AsNoTracking().Where(r => (r as ICompositeBase<TIdOne, TIdTwo>).IdTwo.Equals(businessId));

        /// <inheritdoc/>
        public async virtual Task<IEnumerable<TEntity>> FindAllByCondition(Expression<Func<TEntity, bool>> expression, TIdTwo businessId) => await RepositoryContext.Set<TEntity>().Where(r => (r as ICompositeBase<TIdOne, TIdTwo>).IdTwo.Equals(businessId)).Where(expression).AsNoTracking().ToListAsync();

        /// <inheritdoc/>
        public virtual IQueryable<TEntity> FindAllByConditionQueryable(Expression<Func<TEntity, bool>> expression, TIdTwo businessId) => RepositoryContext.Set<TEntity>().Where(r => (r as ICompositeBase<TIdOne, TIdTwo>).IdTwo.Equals(businessId)).Where(expression).AsNoTracking();

        /// <inheritdoc/>
        public async virtual Task<TEntity> FindFirstByCondition(Expression<Func<TEntity, bool>> expression, TIdTwo businessId) => await RepositoryContext.Set<TEntity>().Where(r => (r as ICompositeBase<TIdOne, TIdTwo>).IdTwo.Equals(businessId)).AsNoTracking().FirstOrDefaultAsync(expression);

        /// <inheritdoc/>
        public async virtual Task<TEntity> FindById(TIdOne idOne, TIdTwo idTwo) => await RepositoryContext.Set<TEntity>().FindAsync(idOne, idTwo);

        //public async virtual Task<TEntity> FindById(TIdOne idOne) => await RepositoryContext.Set<TEntity>().Where(i => i.Id);

        /// <inheritdoc/>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public async virtual Task<bool> Update(TEntity entity, IDbContextTransaction dbTransaction = default)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            RepositoryContext.Set<TEntity>().Update(entity);
            return true;
        }

        /// <inheritdoc/>
        public async virtual Task<bool> Exists(TIdOne idOne, TIdTwo idTwo) => await RepositoryContext.Set<TEntity>().OfType<ICompositeBase<TIdOne, TIdTwo>>().AnyAsync(entity => entity.IdOne.Equals(idOne) && entity.IdTwo.Equals(idTwo));

        /// <inheritdoc/>
        public virtual void SetState(TEntity entity, EntityState entityState = EntityState.Modified) => RepositoryContext.Entry(entity).State = entityState;

        public IQueryable<TEntity> FindAll()
        {
            return RepositoryContext.Set<TEntity>().AsNoTracking();
        }
    }
}
