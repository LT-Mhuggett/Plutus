using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface ITriCompositeRepositoryBase<TEntity, TId1, TId2, TId3, TFormBody, TQueryParameters> : Plutus.Contracts.ITriCompositeRepositoryBase<TEntity, TId1, TId2, TId3>
        where TEntity : TriCompositeBase<TId1, TId2, TId3>, new()
        where TFormBody : FormBody<TEntity>
        where TQueryParameters : TriCompositeQueryParameters<TEntity, TId1, TId2, TId3>
    {
        Task<IEnumerable<TEntity>> FindAllByCondition(TQueryParameters queryParameters, TId2 id2, TId3 id3);
        Task<IQueryable<TEntity>> FindAllByConditionQueryable(TQueryParameters queryParameters, TId2 id2, TId3 id3);
        Task<TEntity> FindFirstByCondition(TQueryParameters queryParameters, TId2 id2, TId3 id3);
    }
}
