using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.Entities.Models;
using Plutus.Repository.Extensions;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers.Bases
{
    public abstract class CompositeApiControllerBaseR<TEntity, TId1, TId2, TQueryParameters> : ControllerBase where TEntity : CompositeBase<TId1, TId2> where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
    {
        protected readonly IRepositoryWrapper RepositoryWrapper;
        protected virtual ICompositeRepositoryBase<TEntity, TId1, TId2> Repository { get; }
        protected readonly IHttpContextAccessor HttpContextAccessor;

        public CompositeApiControllerBaseR(IRepositoryWrapper repositoryWrapper,
            IHttpContextAccessor httpContextAccessor)
        {
            HttpContextAccessor = httpContextAccessor;
            RepositoryWrapper = repositoryWrapper;

            Debug.Assert(HttpContextAccessor.HttpContext != null, "HttpContextAccessor.HttpContext != null");
            var objectId = HttpContextAccessor.HttpContext.User.Claims.First(c =>
                c.Type == "https://schemas.microsoft.com/identity/claims/objectidentifier").Value;
            RepositoryWrapper.SetCurrentUser(objectId);
        }

        /// <summary>
        /// Load a list of records that satisfy <see cref="TQueryParameters"/>
        /// </summary>
        /// <param name="businessId">Business Id taken from Header, of type <see cref="TId2"/></param>
        /// <param name="queryParameters">The parameters to satisfy</param>
        /// <returns>List of records satisfied</returns>
        [Authorize(Actions.ReadThings)]
        [HttpGet("Index")]
        [ApiConventionMethod(typeof(DefaultApiConventions), nameof(DefaultApiConventions.Get))]
        public virtual ActionResult<IEnumerable<TEntity>> Index([FromHeader] TId2 businessId, [FromQuery] TQueryParameters queryParameters)
        {
            if (!queryParameters.ValidCreatedDates)
                return BadRequest("Created Max date cannot be less than Created Min date");

            var entities = PagedList<TEntity>.ToPagedList(
                Repository.FindAllByConditionQueryable(queryParameters.GetExpression().And(e => e.IdTwo.Equals(businessId))).OrderBy(e => e.CreatedAt),
                queryParameters.PageNumber, queryParameters.PageSize, queryParameters.IgnorePagination);

            Response.Headers.Add("X-Pagination", JsonConvert.SerializeObject(entities.MetaData));
            Response.Headers.Add("X-Queryable",
                JsonConvert.SerializeObject(new { queryParameters.MinCreatedDate, queryParameters.MaxCreatedDate }));
            return Ok(entities);
        }

        /// <summary>
        /// Find item by <see cref="TId1"/> and <see cref="TId2"/>
        /// </summary>
        /// <param name="id1">First id of composite key</param>
        /// <param name="businessId">Business Id taken from Header, of type <see cref="TId2"/></param>
        /// <returns>Record that was found; else Not Found</returns>
        [Authorize(Actions.ReadThings)]
        [HttpGet("{id1}")]
        [ApiConventionMethod(typeof(DefaultApiConventions), nameof(DefaultApiConventions.Find))]
        public virtual async Task<ActionResult<TEntity>> FindById([FromRoute] TId1 id1, [FromHeader] TId2 businessId)
        {
            var entity = await Repository.FindById(id1, businessId);
            if (entity == default)
            {
                return NotFound();
            }

            return Ok(entity);
        }
    }
}
