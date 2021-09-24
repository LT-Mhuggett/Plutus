using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.DBService.Controllers
{
    public abstract class ApiControllerBaseCR<TEntity, TId, TQueryParameters> : ApiControllerBaseR<TEntity, TId, TQueryParameters> where TEntity : Base<TId> where TQueryParameters : QueryParameters<TEntity, TId>
    {
        // objectId from tenant
        public ApiControllerBaseCR(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }

        /// <summary>
        /// Post Entity to database
        /// </summary>
        /// <param name="entity">Entity to add to database</param>
        /// <param name="isSync">States that this is a sync only request</param>
        /// <returns>Created Entity</returns>
        [Authorize(Actions.WritePermission)]
        [HttpPost]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Post))]
        public async virtual Task<ActionResult<TEntity>> Post([FromBody] TEntity entity, [FromQuery] bool isSync = false)
        {
            await Repository.Create(entity);
            repositoryWrapper.SetSyncState(isSync);
            await repositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { id = entity.Id }, entity);
        }
    }
}
