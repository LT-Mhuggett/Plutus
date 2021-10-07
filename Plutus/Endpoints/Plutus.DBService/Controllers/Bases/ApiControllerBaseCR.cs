using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers.Bases
{
    public abstract class ApiControllerBaseCR<TEntity, TBody, TId, TQueryParameters> : ApiControllerBaseR<TEntity, TId, TQueryParameters> where TEntity : Base<TId> where TBody : FormBody<TEntity> where TQueryParameters : QueryParameters<TEntity, TId>
    {
        // objectId from tenant
        public ApiControllerBaseCR(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
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
        public virtual async Task<ActionResult<TEntity>> Post([FromBody] TBody body, [FromQuery] bool isSync = false)
        {
            var entity = body.GenerateEntity();
            await Repository.Create(entity);
            RepositoryWrapper.SetSyncState(isSync);
            await RepositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { id = entity.Id }, entity);
        }
    }
}
