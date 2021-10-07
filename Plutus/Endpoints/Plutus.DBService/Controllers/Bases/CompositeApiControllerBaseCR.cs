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
    public abstract class CompositeApiControllerBaseCR<TEntity, TBody, TId1, TId2, TQueryParameters> : CompositeApiControllerBaseR<TEntity, TId1, TId2, TQueryParameters> where TEntity : CompositeBase<TId1, TId2> where TBody : FormBody<TEntity> where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
    {
        public CompositeApiControllerBaseCR(IRepositoryWrapper repositoryWrapper,
            IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }

        /// <summary>
        /// Create the entity in the database
        /// </summary>
        /// <param name="businessId">Business Id taken from Header, of type <see cref="TId2"/></param>
        /// <param name="body">Data to save to database</param>
        /// <param name="isSync">Is the save a sync style save</param>
        /// <returns>The item that was created in to the database</returns>
        [Authorize(Actions.WritePermission)]
        [HttpPost]
        [ApiConventionMethod(typeof(DefaultApiConventions), nameof(DefaultApiConventions.Post))]
        public virtual async Task<ActionResult<TEntity>> Post([FromHeader] TId2 businessId, [FromBody] TBody body, [FromQuery] bool isSync = false)
        {
            var entity = body.GenerateEntity();
            entity.IdTwo = businessId;

            if (!TryValidateModel(entity))
                return BadRequest(ModelState);

            await Repository.Create(entity);
            RepositoryWrapper.SetSyncState(isSync);
            await RepositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { id1 = entity.IdOne, id2 = entity.IdTwo }, entity);
        }
    }
}
