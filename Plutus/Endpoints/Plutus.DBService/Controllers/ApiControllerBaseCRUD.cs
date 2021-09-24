using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    public abstract class ApiControllerBaseCRUD<TEntity, TId, TQueryParameters> : ApiControllerBaseCRU<TEntity, TId, TQueryParameters> where TEntity : Base<TId> where TQueryParameters : QueryParameters<TEntity, TId>
    {

        public ApiControllerBaseCRUD(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor htthttpContextAccessor) : base(repositoryWrapper, htthttpContextAccessor)
        {
        }


        /// <summary>
        /// Delete entity from database
        /// </summary>
        /// <param name="id">Id of entity to delete, of type <see cref="TId"/></param>
        /// <returns>entity of type <see cref="TEntity"/></returns>
        [Authorize(Actions.WritePermission)]
        [HttpDelete("{id}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Delete))]
        public async virtual Task<ActionResult<TEntity>> Delete([FromRoute] TId id)
        {
            var entity = await Repository.FindById(id);
            if (entity == default)
                return NotFound();

            await Repository.Delete(entity);
            await repositoryWrapper.SaveAsync();

            return Ok(entity);
        }
    }
}
