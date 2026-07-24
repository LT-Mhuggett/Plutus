using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using Plutus.Contracts;
using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using System;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers.Bases
{
    public abstract class ApiControllerBaseCR<TEntity, TBody, TId, TQueryParameters> : ApiControllerBaseR<TEntity, TId, TQueryParameters> where TEntity : Base<TId>, new() where TBody : FormBody<TEntity> where TQueryParameters : QueryParameters<TEntity, TId>
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
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPost]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Create))]
        public virtual async Task<ActionResult<TEntity>> Post([FromBody] TBody body, [FromQuery] bool isSync = false)
        {
            Console.WriteLine("Entered POST method:");
            Console.WriteLine(body);
            var entity = body.GenerateEntity();
            Console.WriteLine(entity);
            await Repository.Create(entity);
            RepositoryWrapper.SetSyncState(isSync);
            await RepositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { id = entity.Id }, entity);
        }
    }
}
