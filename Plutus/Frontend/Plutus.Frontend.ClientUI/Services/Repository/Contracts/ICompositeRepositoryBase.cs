using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface ICompositeRepositoryBase<TEntity, TId1, TId2, TFormBody, TQueryParameters> : Plutus.Contracts.ICompositeRepositoryBase<TEntity, TId1, TId2>
        where TEntity : CompositeBase<TId1, TId2>, new()
        where TFormBody : FormBody<TEntity>
        where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
    {
        Task<IEnumerable<TEntity>> FindAllByCondition(TQueryParameters queryParameters, TId2 id2);
        Task<IQueryable<TEntity>> FindAllByConditionQueryable(TQueryParameters queryParameters, TId2 id2);
        Task<TEntity> FindFirstByCondition(TQueryParameters queryParameters, TId2 id2);
    }
}
