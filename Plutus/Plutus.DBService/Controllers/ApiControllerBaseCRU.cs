using Microsoft.AspNetCore.Mvc;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    public abstract class APIControllerBaseCRU<TEntity, TId, TQueryParameters> : ApiControllerBaseRU<TEntity, TId, TQueryParameters> where TEntity : Base<TId> where TQueryParameters : QueryParameters<TEntity, TId> {
       
        public APIControllerBaseCRU(IRepositoryWrapper repositoryWrapper) :base(repositoryWrapper) 
        {
        }


        /// <summary>
        /// Post Entity to database
        /// </summary>
        /// <param name="entity">Entity to add to database</param>
        /// <param name="isSync">States that this is a sync only request</param>
        /// <returns>Created Entity</returns>
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
