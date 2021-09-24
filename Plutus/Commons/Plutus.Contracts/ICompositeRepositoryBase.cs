using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Plutus.Contracts
{
    public interface ICompositeRepositoryBase<TEntity, TIdOne, TIdTwo>
    {

        /// <summary>
        /// Async GET all records from table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <returns>Enumerable List of <see cref="TEntity"/></returns>
        Task<IEnumerable<TEntity>> GetAll();

        IQueryable<TEntity> FindAll();

        /// <summary>
        /// Async GET all records from table, denoted by <see cref="TEntity"/>, as IQueryable
        /// </summary>
        /// <returns>Queryable List of <see cref="TEntity"/></returns>
        IQueryable<TEntity> GetAllQueryable();

        /// <summary>
        /// Async FIND all that satisfies predicate expression from table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <param name="expression">Condition</param>
        /// <returns>Enumerable List of <see cref="TEntity"/></returns>
        Task<IEnumerable<TEntity>> FindAllByCondition(Expression<Func<TEntity, bool>> expression);

        /// <summary>
        /// Async FIND all that satisfies predicate expression from table, denoted by <see cref="TEntity"/>, as IQueryable
        /// </summary>
        /// <param name="expression">Condition</param>
        /// <returns>Queryable List of <see cref="TEntity"/></returns>
        IQueryable<TEntity> FindAllByConditionQueryable(Expression<Func<TEntity, bool>> expression);

        /// <summary>
        /// Async FIND first record that satisfies predicate expression from table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <param name="expression">Condition</param>
        /// <returns>Single <see cref="TEntity"/></returns>
        Task<TEntity> FindFirstByCondition(Expression<Func<TEntity, bool>> expression);

        /// <summary>
        /// Async FIND by ID from table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <param name="idOne">IDOne of the record</param>
        /// <param name="idTwo">IDTwo of the record</param>
        /// <returns>Single <see cref="TEntity"/></returns>
        Task<TEntity> FindById(TIdOne idOne, TIdTwo idTwo);

        //Task<TEntity> FindById(TIdOne idOne);

        /// <summary>
        /// Async CREATE entity to table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <param name="entity">Entity to add to table</param>
        /// <returns>Success of addition</returns>
        Task<bool> Create(TEntity entity);

        /// <summary>
        /// Async UPDATE entity in table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <param name="entity">Entity to update in table</param>
        /// <returns>Success of update</returns>
        Task<bool> Update(TEntity entity);

        /// <summary>
        /// Async DELETE entity from table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <param name="entity">Entity to delete from table</param>
        /// <returns>Success of deletion</returns>
        Task<bool> Delete(TEntity entity);

        /// <summary>
        /// Async does entity EXIST in table, denoted by <see cref="TEntity"/>
        /// </summary>
        /// <param name="id">id to check</param>
        /// <returns>Entity exists or not</returns>
        Task<bool> Exists(TIdOne idOne, TIdTwo idTwo);

        /// <summary>
        /// Manually set the state of an Entity
        /// </summary>
        /// <param name="entity">Entity to change state of</param>
        /// <param name="entityState">State to change to (defaults to <see cref="EntityState.Modified"/></param>
        void SetState(TEntity entity, EntityState entityState = EntityState.Modified);
    }
}
