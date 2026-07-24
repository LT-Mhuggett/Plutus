using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IRepositoryBase<TEntity, TId, TFormBody, TQueryParameters> : Plutus.Contracts.IRepositoryBase<TEntity, TId> 
        where TEntity : Base<TId>, new()
        where TFormBody : FormBody<TEntity>
        where TQueryParameters : QueryParameters<TEntity, TId>
    {
        Task<IEnumerable<TEntity>> FindAllByCondition(TQueryParameters queryParameters);
        Task<IQueryable<TEntity>> FindAllByConditionQueryable(TQueryParameters queryParameters);
        Task<TEntity> FindFirstByCondition(TQueryParameters queryParameters);
    }
}
