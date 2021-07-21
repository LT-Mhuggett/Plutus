using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Plutus.Contracts;
using Plutus.DBService.Extensions;
using Plutus.Entities.Models;
using Plutus.Authentication;
using Plutus.Repository.Extensions;
using Plutus.Repository.QueryParameters;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    public abstract class ApiControllerBaseR<TEntity, TId, TQueryParameters> : ControllerBase where TEntity : Base<TId> where TQueryParameters : QueryParameters<TEntity, TId>
    {
        protected readonly IRepositoryWrapper repositoryWrapper;
        protected virtual IRepositoryBase<TEntity, TId> Repository { get; }

        public ApiControllerBaseR(IRepositoryWrapper repositoryWrapper)
        {
            this.repositoryWrapper = repositoryWrapper;
        }

        /// <summary>
        /// Load a list of records that satisfy <see cref="TQueryParameters"/>
        /// </summary>
        /// <param name="queryParameters">The parameters to satisfy</param>
        /// <returns>List of records satisfied</returns>
        [Authorize(Actions.ReadThings)]
        [HttpGet("Index")]
        [ProducesResponseType(200)]
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

        [Authorize(Actions.ReadThings)]
        [HttpGet("{id}")]
        [ApiConventionMethod(typeof(DefaultApiConventions),
            nameof(DefaultApiConventions.Find))]
        
        public virtual async Task<ActionResult<TEntity>> FindById([FromRoute] TId id)
        {
            var entity = await Repository.FindById(id);
            if(entity == default)
            {
                return NotFound();
            }

            return Ok(entity);
        }
    }
}
