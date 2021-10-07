using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Entities.Models.Interface;
using Plutus.Repository.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers.Bases
{
    public abstract class ApiControllerBaseCRU<TEntity, TBody, TId, TQueryParameters> : ApiControllerBaseCR<TEntity, TBody, TId, TQueryParameters> where TEntity : Base<TId> where TBody : FormBody<TEntity> where TQueryParameters : QueryParameters<TEntity, TId> {
       
        public ApiControllerBaseCRU(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) :base(repositoryWrapper, httpContextAccessor) 
        {
        }

        /// <summary>
        /// Put entity to database
        /// </summary>
        /// <param name="id">Id of entity, of type <see cref="TId"/></param>
        /// <param name="entity">Entity to save, of type <see cref="TEntity"/></param>
        /// <param name="IsSync">States that this is a sync only request</param>
        /// <returns>Entity of type <see cref="TEntity"/></returns>
        [Authorize(Actions.WritePermission)]
        [HttpPut("{id}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Put))]
        public virtual async Task<ActionResult<TEntity>> Put([FromRoute] TId id, [FromBody] TEntity entity, [FromQuery] bool IsSync = false)
        {
            if (!EqualityComparer<TId>.Default.Equals(id, ((IBase<TId>)entity).Id))
                return BadRequest();

            if (IsSync)
            {
                var tempEntity = await Repository.FindById(id);
                if (tempEntity.ModifiedAt > entity.ModifiedAt)
                    return NoContent();

                Repository.SetState(tempEntity, EntityState.Detached);
                tempEntity = null;
            }

            Repository.SetState(entity);

            try
            {
                RepositoryWrapper.SetSyncState(IsSync);
                await RepositoryWrapper.SaveAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(id))
                    return NotFound();
                throw;
            }

            return NoContent();
        }

        /// <summary>
        /// Patch entity to database
        /// </summary>
        /// <param name="id">Id of entity to update</param>
        /// <param name="patchDocument">Data to update entity</param>
        /// <param name="IsSync">States that this is a sync only request</param>
        /// <returns>Entity of type <see cref="TEntity"/></returns>
        [Authorize(Actions.WritePermission)]
        [HttpPatch("{id}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Update))]
        public virtual async Task<ActionResult<TEntity>> Patch([FromRoute] TId id, [FromBody] JsonPatchDocument<TEntity> patchDocument, [FromQuery] bool IsSync = false)
        {
            if (patchDocument == default)
                return BadRequest();

            var entityFromDb = await Repository.FindById(id);
            if (entityFromDb == default)
                return NotFound();

            patchDocument.ApplyTo(entityFromDb, ModelState);

            if (!TryValidateModel(entityFromDb))
                return BadRequest(ModelState);

            Repository.SetState(entityFromDb);

            try
            {
                RepositoryWrapper.SetSyncState(IsSync);
                await RepositoryWrapper.SaveAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await Repository.Exists(id))
                    return NotFound();
            }

            return NoContent();
        }
    }
}
