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
    public abstract class CompositeApiControllerBaseCRUD<TEntity, TBody, TId1, TId2, TQueryParameters> : CompositeApiControllerBaseCRU<TEntity, TBody, TId1, TId2, TQueryParameters> where TEntity : CompositeBase<TId1, TId2> where TBody : FormBody<TEntity> where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
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
        [Authorize(Actions.WritePermission)]
        [HttpDelete("{id1}/{id2}")]
        [ApiConventionMethod(typeof(DefaultApiConventions), nameof(DefaultApiConventions.Delete))]
        public virtual async Task<ActionResult<TEntity>> Delete([FromRoute] TId1 id1, [FromRoute] TId2 id2)
        {
            var entity = await Repository.FindById(id1, id2);
            if (entity == default)
                return NotFound();

            await Repository.Delete(entity);
            await RepositoryWrapper.SaveAsync();

            return Ok(entity);
        }
    }
}
