using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers.Bases
{
    public abstract class CompositeApiControllerBaseCRUD<TEntity, TBody, TId1, TId2, TQueryParameters> : CompositeApiControllerBaseCRU<TEntity, TBody, TId1, TId2, TQueryParameters> where TEntity : CompositeBase<TId1, TId2>, new() where TBody : FormBody<TEntity> where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
    {
        public CompositeApiControllerBaseCRUD(IRepositoryWrapper repositoryWrapper,
            IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }

        /// <summary>
        /// Delete entity from database
        /// </summary>
        /// <param name="id1">Id1 of entity to delete, of type <see cref="TId1"/></param>
        /// <param name="id2">Id2 of entity to delete, of type <see cref="TId2"/></param>
        /// <returns>Entity of type <see cref="TEntity"/></returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpDelete("{id1}/{id2}")]
        [ApiConventionMethod(typeof(APIConventions), 
                             nameof(APIConventions.Delete))]
        public virtual async Task<ActionResult<TEntity>> Delete([FromRoute] TId1 id1, [FromRoute] TId2 businessId)
        {
            if (id1.Equals(default(TId1)) || businessId.Equals(default(TId2)))
                return BadRequest("Record ID and/or Business ID not provided");

            var entity = await Repository.FindById(id1, businessId);
            if (entity == default)
                return NotFound();

            await Repository.Delete(entity);
            await RepositoryWrapper.SaveAsync();

            return Ok(entity);
        }
    }
}
