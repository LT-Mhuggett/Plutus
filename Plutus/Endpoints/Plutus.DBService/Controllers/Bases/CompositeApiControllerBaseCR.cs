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
using System;
using DocumentFormat.OpenXml.Office2010.Excel;

namespace Plutus.DBService.Controllers.Bases
{
    public abstract class CompositeApiControllerBaseCR<TEntity, TBody, TId1, TId2, TQueryParameters> : CompositeApiControllerBaseR<TEntity, TId1, TId2, TQueryParameters> where TEntity : CompositeBase<TId1, TId2>, new() where TBody : FormBody<TEntity> where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
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
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIWrite:Name")]
        [HttpPost]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Post))]
        public virtual async Task<ActionResult<TEntity>> Post([FromHeader] TId2 businessId, [FromBody] TBody body, [FromQuery] bool isSync = false)
        {
            if (businessId.Equals(default(TId2)))
                return BadRequest("Business ID not provided");

            Console.WriteLine("Entered Composite POST:");
            var entity = body.GenerateEntity();
            entity.IdTwo = businessId;

            //if (!TryValidateModel(entity))
            //    return BadRequest(ModelState);

            await Repository.Create(entity);
            RepositoryWrapper.SetSyncState(isSync);
            await RepositoryWrapper.SaveAsync();

            return CreatedAtAction("FindById", new { id1 = entity.IdOne, id2 = entity.IdTwo }, entity);
        }
    }
}
