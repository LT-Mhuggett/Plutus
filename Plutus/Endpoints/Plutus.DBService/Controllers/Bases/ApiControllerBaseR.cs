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
    [ApiController]
    public abstract class ApiControllerBaseR<TEntity, TId, TQueryParameters> : ControllerBase where TEntity : Base<TId> where TQueryParameters : QueryParameters<TEntity, TId>
    {
        protected readonly IHttpContextAccessor HttpContextAccessor;
        protected readonly IRepositoryWrapper RepositoryWrapper;
        public ApiControllerBaseR(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor)
        {
            HttpContextAccessor = httpContextAccessor;
            RepositoryWrapper = repositoryWrapper;
            RepositoryWrapper.SetCurrentUser(HttpContextAccessor.HttpContext.User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Value);
        }

        protected virtual IRepositoryBase<TEntity, TId> Repository { get; }
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("{id}")]
        [ApiConventionMethod(typeof(APIConventions),
                                     nameof(APIConventions.Find))]

        public virtual async Task<ActionResult<TEntity>> FindById([FromRoute] TId id, [FromQuery] TQueryParameters queryParameters)
        {
            var entity = await Repository.FindById(id);
            if (entity == default)
                return NotFound();

            return Ok(entity);
        }

        /// <summary>
        /// Load a list of records that satisfy <see cref="TQueryParameters"/>
        /// </summary>
        /// <param name="queryParameters">The parameters to satisfy</param>
        /// <returns>List of records satisfied</returns>
        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("Index")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Index))]
        public virtual ActionResult<IEnumerable<TEntity>> Index([FromQuery] TQueryParameters queryParameters)
        {
            if (!queryParameters.ValidCreatedDates)
                return BadRequest("Created Max date cannot be less than Created min date");

            var entities = PagedList<TEntity>.ToPagedList(Repository.FindAllByConditionQueryable(queryParameters.GetExpression()).OrderBy(e => e.CreatedAt),
                                                          queryParameters.PageNumber, queryParameters.PageSize, queryParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(entities.MetaData));
            Response.Headers.Add("X-Queryable", JsonConvert.SerializeObject(new { queryParameters.MinCreatedDate, queryParameters.MaxCreatedDate }));
            return Ok(entities);
        }
    }
}
