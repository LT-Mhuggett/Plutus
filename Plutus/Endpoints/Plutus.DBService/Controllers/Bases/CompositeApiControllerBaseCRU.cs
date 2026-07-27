using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.Resource;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Entities.Models.Interface;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers.Bases
{
    public abstract class CompositeApiControllerBaseCRU<TEntity, TBody, TId1, TId2, TQueryParameters> : CompositeApiControllerBaseCR<TEntity, TBody, TId1, TId2, TQueryParameters> where TEntity : CompositeBase<TId1, TId2>, new() where TBody : FormBody<TEntity> where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
    {
        public CompositeApiControllerBaseCRU(IRepositoryWrapper repositoryWrapper,
            IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }

        /// <summary>
        /// Put entity to database
        /// </summary>
        /// <param name="id1">Id1 of entity, of type <see cref="TId1"/></param>
        /// <param name="businessId">Business Id taken from Header, of type <see cref="TId2"/></param>
        /// <param name="entity">Entity to save, of type <see cref="TEntity"/></param>
        /// <param name="isSync">States that this is a sync only request</param>
        /// <returns>Entity of type <see cref="TEntity"/></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPut("{id1}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Put))]
        public virtual async Task<ActionResult<TEntity>> Put([FromRoute] TId1 id1, [FromHeader] TId2 businessId,
            [FromBody] TEntity entity, [FromQuery] bool isSync = false)
        {
            if (id1.Equals(default(TId1)) || businessId.Equals(default(TId2)))
                return BadRequest("Record ID and/or Business ID not provided");

            if (!EqualityComparer<TId1>.Default.Equals(id1, ((ICompositeBase<TId1, TId2>)entity).IdOne) &&
                !EqualityComparer<TId2>.Default.Equals(businessId, ((ICompositeBase<TId1, TId2>)entity).IdTwo))
                return BadRequest("Entity keys do not match provided IDs");

            if (isSync)
            {
                var tempEntity = await Repository.FindById(id1, businessId);
                if (tempEntity.ModifiedAt > entity.ModifiedAt)
                    return NoContent();

                Repository.SetState(tempEntity, EntityState.Detached);
                tempEntity = null;
            }

            Repository.SetState(entity);

            try
            {
                RepositoryWrapper.SetSyncState(isSync);
                await RepositoryWrapper.SaveAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(id1, businessId))
                    return NotFound();
                throw;
            }

            return NoContent();
        }

        /// <summary>
        /// Patch entity to database
        /// </summary>
        /// <param name="id1">Id1 of entity, of type <see cref="TId1"/></param>
        /// <param name="businessId">Business Id taken from Header, of type <see cref="TId2"/></param>
        /// <param name="patchDocument">Data to update entity</param>
        /// <param name="isSync">States that this is a sync only request</param>
        /// <returns>Entity of type <see cref="TEntity"/></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPatch("{id1}")]
        [ApiConventionMethod(typeof(APIConventions), 
                             nameof(APIConventions.Patch))]
        public virtual async Task<ActionResult<TEntity>> Patch([FromRoute] TId1 id1, [FromHeader] TId2 businessId,
            [FromBody] JsonPatchDocument<TEntity> patchDocument, [FromQuery] bool isSync = false)
        {
            if (id1.Equals(default(TId1)) || businessId.Equals(default(TId2)))
                return BadRequest("Record ID and/or Business ID not provided");

            if (patchDocument == default)
                return BadRequest("Patch Document not provided");

            var entityFromDb = await Repository.FindById(id1, businessId);
            if (entityFromDb == default)
                return NotFound();

            patchDocument.ApplyTo(entityFromDb, ModelState);

            if (!TryValidateModel(entityFromDb))
                return BadRequest(ModelState);

            Repository.SetState(entityFromDb);

            try
            {
                RepositoryWrapper.SetSyncState(isSync);
                await RepositoryWrapper.SaveAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(id1, businessId))
                    return NotFound();
            }

            return NoContent();
        }
    }
}
