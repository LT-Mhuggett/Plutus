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
    public abstract class ApiControllerBaseCRUD<TEntity, TBody, TId, TQueryParameters> : ApiControllerBaseCRU<TEntity, TBody, TId, TQueryParameters> where TEntity : Base<TId> where TBody : FormBody<TEntity> where TQueryParameters : QueryParameters<TEntity, TId>
    {

        public ApiControllerBaseCRUD(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }


        /// <summary>
        /// Delete entity from database
        /// </summary>
        /// <param name="id">Id of entity to delete, of type <see cref="TId"/></param>
        /// <returns>Entity of type <see cref="TEntity"/></returns>
        [Authorize(Actions.WritePermission)]
        [HttpDelete("{id}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Delete))]
        public virtual async Task<ActionResult<TEntity>> Delete([FromRoute] TId id)
        {
            var entity = await Repository.FindById(id);
            if (entity == default)
                return NotFound();

            await Repository.Delete(entity);
            await RepositoryWrapper.SaveAsync();

            return Ok(entity);
        }
    }
}
