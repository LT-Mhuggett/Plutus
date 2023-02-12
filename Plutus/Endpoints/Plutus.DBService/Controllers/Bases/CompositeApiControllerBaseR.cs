using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;
using Newtonsoft.Json;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using Plutus.Repository.QueryParameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers.Bases
{
    public abstract class CompositeApiControllerBaseR<TEntity, TId1, TId2, TQueryParameters> : ControllerBase where TEntity : CompositeBase<TId1, TId2> where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
    {
        protected readonly IHttpContextAccessor HttpContextAccessor;
        protected readonly IRepositoryWrapper RepositoryWrapper;
        public CompositeApiControllerBaseR(IRepositoryWrapper repositoryWrapper,
            IHttpContextAccessor httpContextAccessor)
        {
            HttpContextAccessor = httpContextAccessor;
            RepositoryWrapper = repositoryWrapper;

            RepositoryWrapper.SetCurrentUser(HttpContextAccessor.HttpContext.User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Value);
        }

        protected virtual ICompositeRepositoryBase<TEntity, TId1, TId2> Repository { get; }
        /// <summary>
        /// Find item by <see cref="TId1"/> and <see cref="TId2"/>
        /// </summary>
        /// <param name="id1">First id of composite key</param>
        /// <param name="businessId">Business Id taken from Header, of type <see cref="TId2"/></param>
        /// <returns>Record that was found; else Not Found</returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("{id1}")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Find))]
        public virtual async Task<ActionResult<TEntity>> FindById([FromRoute] TId1 id1, [FromHeader] TId2 businessId)
        {
            if (id1.Equals(default(TId1)) || businessId.Equals(default(TId2)))
                return BadRequest("Record ID and/or Business ID not provided");

            var entity = await Repository.FindById(id1, businessId);
            if (entity == default)
            {
                return NotFound();
            }

            return Ok(entity);
        }

        /// <summary>
        /// Load a list of records that satisfy <see cref="TQueryParameters"/>
        /// </summary>
        /// <param name="businessId">Business Id taken from Header, of type <see cref="TId2"/></param>
        /// <param name="queryParameters">The parameters to satisfy</param>
        /// <returns>List of records satisfied</returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("Index")]
        [ApiConventionMethod(typeof(APIConventions), 
                             nameof(APIConventions.Index))]
        public virtual ActionResult<IEnumerable<TEntity>> Index([FromHeader] TId2 businessId, [FromQuery] TQueryParameters queryParameters)
        {
            if(businessId.Equals(default(TId2)))
                return BadRequest("Business ID not provided");
            if (!queryParameters.ValidCreatedDates)
                return BadRequest("Created Max date cannot be less than Created Min date");

            var entities = PagedList<TEntity>.ToPagedList(
                Repository.FindAllByConditionQueryable(queryParameters.GetExpression(), businessId).OrderBy(e => e.CreatedAt),
                queryParameters.PageNumber, queryParameters.PageSize, queryParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(entities.MetaData));
            Response.Headers.Add("X-Queryable",
                JsonConvert.SerializeObject(new { queryParameters.MinCreatedDate, queryParameters.MaxCreatedDate }));
            return Ok(entities);
        }
    }
}
